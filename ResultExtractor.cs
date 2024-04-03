using System.Data.Common;
using System.Reflection;
// ReSharper disable MemberCanBePrivate.Global

namespace Sporm;

public static class TypeChecker
{
    public static bool IsSystem(this Type t) => t.Namespace == nameof(System) && t != typeof(object);
    public static bool IsTaskResult(this Type t) => t.GetGenericTypeDefinition() == typeof(Task<>);
    public static bool IsVoid(this Type t) => t == typeof(void);
    public static bool IsAsyncVoid(this Type t) => t == typeof(Task);
    public static bool IsEnumerable(this Type t) => t.GetGenericTypeDefinition() == typeof(IEnumerable<>);

    public static bool IsAsyncEnumerable(this Type t) => t.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>);
    public static Type GetInnerType(this Type t) => t.GetGenericArguments()[0];

    public static bool IsSystemTaskResult(this Type t) =>
        t.GetInnerType() is { } taskResultType && taskResultType.IsSystem();

    public static bool IsUntypedDictionary(this Type t) => t == typeof(Dictionary<string, object?>);

    public static bool IsAsyncUntypedDictionary(this Type t) =>
        t.IsTaskResult() && t.GetInnerType() is { } taskResultType && taskResultType.IsUntypedDictionary();

    public static bool IsDynamicObject(this Type t) => t == typeof(object);
}

public class ResultExtractor
{
    private readonly Configuration _configuration;

    public ResultExtractor(Configuration configuration)
    {
        _configuration = configuration;
        RegisterExtractors();
    }

    private DbDataReader _reader = null!;

    private static readonly Dictionary<Predicate<Type>, Func<DbCommand, Type, object?>> Extractors = new();

    public static void Register(Predicate<Type> predicate, Func<DbCommand, Type, object?> func)
    {
        Extractors[predicate] = func;
    }

    private Func<DbCommand, Type, object?> InvokeGeneric(string name) =>
        (command, t) => typeof(ResultExtractor)
            .GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(t).Invoke(this, [command]);

    private Func<DbCommand, Type, object?> InvokeInnerGeneric(string name) =>
        (command, t) => typeof(ResultExtractor)
            .GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!
            .MakeGenericMethod(t.GetInnerType()).Invoke(this, [command]);

    public static T? ExtractTyped<T>(DbCommand command) =>
        (T?)Extractors.First(item => item.Key(typeof(T))).Value(command, typeof(T));

    public object? Extract(DbCommand command, Type t) =>
        typeof(ResultExtractor).GetMethod(nameof(ExtractTyped))!.MakeGenericMethod(t).Invoke(null, [command]);


    public void RegisterExtractors()
    {
        Register(TypeChecker.IsUntypedDictionary, ExtractDictionary);
        Register(TypeChecker.IsAsyncUntypedDictionary, ExtractDictionaryAsync);
        Register(TypeChecker.IsSystem, InvokeGeneric(nameof(ExtractPrimitive)));
        Register(TypeChecker.IsSystemTaskResult, InvokeInnerGeneric(nameof(ExtractPrimitiveAsync)));
        Register(TypeChecker.IsEnumerable, InvokeInnerGeneric(nameof(ExtractEnumerable)));
        Register(TypeChecker.IsAsyncEnumerable, InvokeInnerGeneric(nameof(ExtractAsyncEnumerable)));
        Register(TypeChecker.IsDynamicObject, ExtractDynamic);
    }

    private Dictionary<string, object?>? ExtractDictionary(DbCommand command, Type t)
    {
        _reader = command.ExecuteReader();
        if (!_reader.Read()) return null;
        var fields = Enumerable.Range(0, _reader.FieldCount).Select(i => _reader.GetName(i))
            .ToArray();

        var instance = fields.ToDictionary(name => name,
            name => _reader[name] is DBNull ? null : _reader[name]);
        return instance;
    }

    private async Task<Dictionary<string, object?>?> ExtractDictionaryAsync(DbCommand command, Type t)
    {
        _reader = await command.ExecuteReaderAsync();
        if (!await _reader.ReadAsync()) return null;
        var fields = Enumerable.Range(0, _reader.FieldCount).Select(i => _reader.GetName(i))
            .ToArray();

        var instance = fields.ToDictionary(name => name,
            name => _reader[name] is DBNull ? null : _reader[name]);
        return instance;
    }

    private static T? ExtractPrimitive<T>(DbCommand command)
    {
        var result = command.ExecuteScalar();
        return result is DBNull ? default : (T?)result;
    }

    private static async Task<T?> ExtractPrimitiveAsync<T>(DbCommand command)
    {
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? default : (T?)result;
    }

    private IEnumerable<T>? ExtractEnumerable<T>(DbCommand command) where T: new()
    {
        _reader = command.ExecuteReader();
        if (typeof(T).IsDynamicObject()) 
            return (IEnumerable<T>?) Utils.GetIteratorDynamic(_reader, _configuration);
        if (typeof(T).IsUntypedDictionary()) 
            return (IEnumerable<T>?) Utils.GetIteratorDictionary(_reader, _configuration);
        return (IEnumerable<T>?) typeof(Utils).GetMethod(nameof(Utils.GetIterator),
                BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T))
            .Invoke(null, [_reader, _configuration]);
    }

    private IAsyncEnumerable<T>? ExtractAsyncEnumerable<T>(DbCommand command) where T: new()
    {
        _reader = command.ExecuteReader();
        if (typeof(T).IsDynamicObject()) 
            return (IAsyncEnumerable<T>?) Utils.GetIteratorDynamicAsync(_reader, _configuration);
        if (typeof(T).IsUntypedDictionary()) 
            return (IAsyncEnumerable<T>?) Utils.GetIteratorDictionaryAsync(_reader, _configuration);
        return (IAsyncEnumerable<T>?) typeof(Utils).GetMethod(nameof(Utils.GetIteratorAsync),
                BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(typeof(T))
            .Invoke(null, [_reader, _configuration]);
    }

    private object? ExtractDynamic(DbCommand command, Type t)
    {
        using (_reader = command.ExecuteReader())
        {
            if (!_reader.Read()) return null;

            var fields = Enumerable.Range(0, _reader.FieldCount).Select(i => _reader.GetName(i))
                .ToArray();

            if (t == typeof(object))
            {
                var builder =
                    new DynamicTypeBuilder(Utils.AnonymousTypePrefix + _reader.GetHashCode());
                foreach (var name in fields)
                {
                    builder.AddProperty(name, _reader.GetFieldType(_reader.GetOrdinal(name)));
                }

                var type = builder.CreateType();
                var instance = Activator.CreateInstance(type);
                foreach (var name in fields)
                {
                    type.GetProperty(name)!.SetValue(instance,
                        _reader[name] is DBNull ? null : _reader[name], null);
                }

                return instance;
            }
            else
            {
                var instance = Activator.CreateInstance(t);
                foreach (var prop in t.GetProperties())
                {
                    if (Array.IndexOf(fields, prop.Name) != -1)
                    {
                        prop.SetValue(instance,
                            _reader[prop.Name] is DBNull ? null : _reader[prop.Name], null);
                    }
                }

                return instance;
            }
        }
    }
}