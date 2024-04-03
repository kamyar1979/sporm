using System.Reflection;

namespace Sporm;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

internal static class Utils
{
    internal const string ReturnValue = "RetVal";
    internal const string AnonymousTypePrefix = "anonym_";
    internal const string OnTheFlyAssemblyName = "OnTheFly";
    internal const string DynamicModuleName = "AnonymousTypes";
    internal const string GetPropertyPrefix = "get_";
    internal const string SetPropertyPrefix = "set_";
    internal const string ValidNamePattern = @"^[^\d]\w*$";
    internal const string AsyncMethodPostfix = "Async";
    internal const string DeriveParameters = "DeriveParameters";

    internal static IEnumerable<Dictionary<string, object?>> GetIteratorDictionary(DbDataReader reader, 
        Configuration configuration)
    {
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        while (reader.Read())
        {
            yield return fields.ToDictionary(name => configuration.Deflector is {} deflector ? deflector(name) : name
                , name => reader[name] is DBNull ? null : reader[name]);
        }

        reader.Close();
    }

    internal static async IAsyncEnumerable<Dictionary<string, object?>> GetIteratorDictionaryAsync(DbDataReader reader,
        Configuration configuration)
    {
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        while (await reader.ReadAsync())
        {
            yield return fields.ToDictionary(name => configuration.Deflector is {} deflector ? deflector(name) : name,
                name => reader[name] is DBNull ? null : reader[name]);
        }

        await reader.CloseAsync();
    }

    internal static IEnumerable<T> GetIterator<T>(DbDataReader reader,
        Configuration configuration) where T : new()
    {
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        while (reader.Read())
        {
            object instance = new T();
            foreach (var prop in typeof(T).GetProperties())
            {
                if (!DbNameAttribute.TryGetName(prop, out var dbFieldName))
                {
                    if (configuration.Inflector != null && dbFieldName != null)
                        dbFieldName = configuration.Inflector(dbFieldName);
                }

                if (dbFieldName != null && Array.IndexOf(fields, dbFieldName) != -1)
                {
                    prop.SetValue(instance, reader[dbFieldName] is DBNull ? null : reader[dbFieldName]);
                }
            }

            yield return (T)instance;
        }

        reader.Close();
    }

    internal static async IAsyncEnumerable<T> GetIteratorAsync<T>(DbDataReader reader,
        Configuration configuration) where T : new()
    {
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        while (await reader.ReadAsync())
        {
            object instance = new T();
            foreach (var prop in typeof(T).GetProperties())
            {
                if (!DbNameAttribute.TryGetName(prop, out var dbFieldName))
                {
                    if (configuration.Inflector != null && dbFieldName != null)
                        dbFieldName = configuration.Inflector(dbFieldName);
                }

                if (dbFieldName != null && Array.IndexOf(fields, dbFieldName) != -1)
                {
                    prop.SetValue(instance, reader[dbFieldName] is DBNull ? null : reader[dbFieldName]);
                }
            }

            yield return (T)instance;
        }

        await reader.CloseAsync();
    }

    internal static IEnumerable<object?> GetIteratorDynamic(IDataReader reader,
        Configuration configuration)
    {
        var builder = new DynamicTypeBuilder(AnonymousTypePrefix + reader.GetHashCode());
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }
        
        foreach (var name in fields)
        {
            var propertyName = configuration.Deflector is { } deflector ? deflector(name) : name;
            builder.AddProperty(propertyName, reader.GetFieldType(reader.GetOrdinal(name)));
        }

        var type = builder.CreateType();
        while (reader.Read())
        {
            var instance = Activator.CreateInstance(type);
            foreach (var name in fields)
            {
                var propertyName = configuration.Deflector is { } deflector ? deflector(name) : name;
                type.GetProperty(propertyName)?.SetValue(instance, reader[name] is DBNull ? null : reader[name], null);
            }

            yield return instance;
        }

        reader.Close();
    }

    internal static async IAsyncEnumerable<object?> GetIteratorDynamicAsync(DbDataReader reader,
        Configuration configuration)
    {
        var builder = new DynamicTypeBuilder(AnonymousTypePrefix + reader.GetHashCode());
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }
        
        foreach (var name in fields)
        {
            var propertyName = configuration.Deflector is { } deflector ? deflector(name) : name;
            builder.AddProperty(propertyName, reader.GetFieldType(reader.GetOrdinal(name)));
        }

        var type = builder.CreateType();
        while (await reader.ReadAsync())
        {
            var instance = Activator.CreateInstance(type);
            foreach (var name in fields)
            {
                var propertyName = configuration.Deflector is { } deflector ? deflector(name) : name;
                type.GetProperty(propertyName)?.SetValue(instance, reader[name] is DBNull ? null : reader[name], null);
            }

            yield return instance;
        }

        await reader.CloseAsync();
    }

    internal static DbType ToClrType(this MemberInfo type, Configuration configuration)
    {
        if (Enum.TryParse<DbType>(type.Name.Replace("&", ""), out var dbType))
        {
            return dbType;
        }

        if (type == typeof(TimeOnly)) return DbType.Time;

        return configuration.TypeResolver?.Invoke(type) ?? DbType.String;
    }
}