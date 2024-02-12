using System.Reflection;

namespace Sporm;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

public record struct DatabaseProvider(
    string ConnectionString,
    string ProviderName,
    Func<string, string>? Inflector,
    Func<MemberInfo, DbType>? TypeResolver);

internal static class Utils
{
    internal const string ReturnValue = "RetVal";
    internal const string AnonymousTypePrefix = "anonym_";
    internal const string OnTheFlyAssemblyName = "OnTheFly";
    internal const string DynamicModuleName = "AnonymousTypes";
    internal const string GetPropertyPrefix = "get_";
    internal const string SetPropertyPrefix = "set_";
    internal const string ValidNamePattern = @"^[^\d]\w*$";

    internal static IEnumerable<Dictionary<string, object?>> GetIteratorDictionary(DbDataReader reader)
    {
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        while (reader.Read())
        {
            yield return fields.ToDictionary(name => name, name => reader[name] is DBNull ? null : reader[name]);
        }

        reader.Close();
    }

    internal static IEnumerable<T> GetIterator<T>(IDataReader reader,
        Func<string, string>? fieldInflector) where T : new()
    {
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        while (reader.Read())
        {
            var instance = new T();
            foreach (var prop in typeof(T).GetProperties())
            {
                if (!DbNameAttribute.TryGetName(prop, out var dbFieldName))
                {
                    if (fieldInflector != null && dbFieldName != null)
                        dbFieldName = fieldInflector(dbFieldName);
                }

                if (dbFieldName != null && Array.IndexOf(fields, dbFieldName) != -1)
                {
                    prop.SetValue(instance, reader[dbFieldName] is DBNull ? null : reader[dbFieldName], null);
                }
            }

            yield return instance;
        }

        reader.Close();
    }

    internal static IEnumerable<object?> GetIteratorDynamic(IDataReader reader)
    {
        var builder = new DynamicTypeBuilder(AnonymousTypePrefix + reader.GetHashCode());
        var fields = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            fields[i] = reader.GetName(i);
        }

        foreach (var name in fields)
        {
            builder.AddProperty(name, reader.GetFieldType(reader.GetOrdinal(name)));
        }

        var type = builder.CreateType();
        while (reader.Read())
        {
            var instance = Activator.CreateInstance(type);
            foreach (var name in fields)
            {
                type.GetProperty(name)?.SetValue(instance, reader[name] is DBNull ? null : reader[name], null);
            }

            yield return instance;
        }

        reader.Close();
    }
    
    internal static DbType ToClrType(this MemberInfo type)
    {
        if(Enum.TryParse<DbType>(type.Name.Replace("&", ""), out var dbType))
        {
            return dbType;
        }

        return type == typeof(TimeOnly) ? DbType.Time : DbType.String;
    }
}