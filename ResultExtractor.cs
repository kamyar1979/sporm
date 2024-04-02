using System.Collections;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sporm;

public class ResultExtractor
{
    private Configuration _configuration;
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

    public static T? ExtractTyped<T>(DbCommand command) =>
        (T?)Extractors.First(item => item.Key(typeof(T))).Value(command, typeof(T));


    public object? Extract(DbCommand command, Type t) =>
        typeof(ResultExtractor).GetMethod(nameof(ExtractTyped))!.MakeGenericMethod(t).Invoke(null, [command]);


    public void RegisterExtractors()
    {
        Register(t => t == typeof(Task<Dictionary<string, object?>?>), ExtractDictionaryAsync);
        Register(t => t == typeof(Dictionary<string, object?>), ExtractDictionary);
        Register(t => t.Namespace == nameof(System) && t != typeof(object), ExtractPrimitive);
        Register(t => t.BaseType == typeof(Task) && t.GetGenericArguments()[0].Namespace == nameof(System) &&
                      t.GetGenericArguments()[0] != typeof(object), ExtractPrimitiveAsync);
        Register(t => t.GetInterface(nameof(IEnumerable<object>)) != null, ExtractEnumerable);
        Register(t => t == typeof(object), ExtractDynamic);
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

    private static object? ExtractPrimitive(DbCommand command, Type t)
    {
        var result = command.ExecuteScalar();
        return result is DBNull ? default : Convert.ChangeType(result, t);
    }


    private static async Task<object?> ExtractPrimitiveAsync(DbCommand command, Type t)
    {
        var result = await command.ExecuteScalarAsync();
        return result is DBNull ? default : Convert.ChangeType(result, t);
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
        return (IEnumerable?) typeof(Utils).GetMethod(nameof(Utils.GetIterator),
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