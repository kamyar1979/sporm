using System.Collections;
using System.Data.Common;
using System.Reflection;

namespace Sporm;

public static class TypeChecker
{
    public static bool IsPrimitive(this Type t) => t.Namespace == nameof(System) && t != typeof(object);
    public static bool IsTaskResult(this Type t) => t.BaseType == typeof(Task);
    public static bool IsVoid(this Type t) => t == typeof(void);
    public static bool IsAsyncVoid(this Type t) => t == typeof(Task);
    public static bool IsEnumerable(this Type t) => t.GetInterface(nameof(IEnumerable<object>)) != null;
    public static Type? GetTaskResultType(this Type t) => t.IsTaskResult() ? t.GetGenericArguments()[0] : null;

    public static bool IsPrimitiveTaskResult(this Type t) =>
        t.GetTaskResultType() is { } taskResultType && taskResultType.IsPrimitive();

    public static bool IsUntypedDictionary(this Type t) => t == typeof(Dictionary<string, object?>);

    public static bool IsAsyncUntypedDictionary(this Type t) =>
        t.GetTaskResultType() is { } taskResultType && taskResultType.IsUntypedDictionary();

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
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(t).Invoke(this, [command]);

    private Func<DbCommand, Type, object?> InvokeAsyncGeneric(string name) =>
        (command, t) => typeof(ResultExtractor)
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(t.GetTaskResultType()!).Invoke(this, [command]);

    public static T? ExtractTyped<T>(DbCommand command) =>
        (T?)Extractors.First(item => item.Key(typeof(T))).Value(command, typeof(T));

    public object? Extract(DbCommand command, Type t) =>
        typeof(ResultExtractor).GetMethod(nameof(ExtractTyped))!.MakeGenericMethod(t).Invoke(null, [command]);


    public void RegisterExtractors()
    {
        Register(TypeChecker.IsUntypedDictionary, ExtractDictionaryAsync);
        Register(TypeChecker.IsAsyncUntypedDictionary, ExtractDictionary);
        Register(TypeChecker.IsPrimitive, InvokeGeneric(nameof(ExtractPrimitive)));
        Register(TypeChecker.IsPrimitiveTaskResult, InvokeAsyncGeneric(nameof(ExtractPrimitiveAsync)));
        Register(TypeChecker.IsEnumerable, ExtractEnumerable);
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

    private IEnumerable? ExtractEnumerable(DbCommand command, Type t)
    {
        _reader = command.ExecuteReader();
        if (!t.IsGenericType ||
            t.GetGenericArguments()[0] == typeof(object)) return Utils.GetIteratorDynamic(_reader, _configuration);
        if (t.GetGenericArguments()[0] ==
            typeof(Dictionary<string, object>))
            return Utils.GetIteratorDictionary(_reader);
        var type = t.GetGenericArguments()[0];
        return (IEnumerable?)typeof(Utils).GetMethod(nameof(Utils.GetIterator),
                BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type)
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