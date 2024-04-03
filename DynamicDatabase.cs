using System.Reflection;

namespace Sporm;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Dynamic;

/// <summary>
/// This is a dynamic class representing a pseud database which its stored procedures is callable via method names.
/// </summary>
public class DynamicDatabase : DynamicObject
{
    private readonly DbConnection _connection = null!;
    private DbDataReader _reader = null!;
    private readonly Configuration _configuration;

    public DynamicDatabase(Configuration configuration)
    {
        if (configuration.ProviderFactory.CreateConnection() is not { } connection) return;
        _connection = connection;
        _connection.ConnectionString = configuration.ConnectionString;
        _configuration = configuration;
    }
    
    /// <summary>
    /// This is Microsoft standard way for capturing any method call in a dynamic object.
    /// </summary>
    /// <param name="binder"></param>
    /// <param name="args"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        result = null;

        _connection.Open();

        var methodName = binder.Name;
        var returnAsResult = methodName.EndsWith('_');
        if (methodName.EndsWith('_'))
        {
            methodName = methodName[..^1];
        }

        if (_configuration.ProviderFactory.CreateCommand() is not { } command) return false;
        command.Connection = _connection;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = methodName;
        if (_configuration.Inflector != null)
            command.CommandText = _configuration.Inflector(command.CommandText);


        var i = 0;

        if (binder.CallInfo.ArgumentNames.Count == 0)
        {
            var builder = _configuration.ProviderFactory.CreateCommandBuilder();
            builder?.GetType().GetMethod("DeriveParameters")?.Invoke(null, [command]);

            var j = 0;
            for (i = 0; i < binder.CallInfo.ArgumentCount; i++)
            {
                while (command.Parameters[i + j].Direction != ParameterDirection.Input
                       || command.Parameters.Count == i + j) j++;
                command.Parameters[i + j].Value = args![i];
            }
        }
        else
        {
            foreach (var item in binder.CallInfo.ArgumentNames)
            {
                if (_configuration.ProviderFactory.CreateParameter() is not { } param) continue;
                param.ParameterName = item;
                if (_configuration.Inflector != null)
                    param.ParameterName = _configuration.Inflector(param.ParameterName);
                param.Direction = ParameterDirection.Input;
                param.DbType = args![i]!.GetType().ToClrType(_configuration);
                param.Value = args[i];
                command.Parameters.Add(param);
                i++;
            }
        }

        var returnType = binder.GetGenericTypeArguments()?.Count > 0
            ? binder.GetGenericTypeArguments()?[0]
            : typeof(object);

        if (returnType != null && returnType != typeof(void))
        {
            if (!returnAsResult)
            {
                if (returnType == typeof(Dictionary<string, object>))
                {
                    var reader = command.ExecuteReader();
                    if (!reader.Read()) return true;
                    var fields = new string[reader.FieldCount];
                    for (i = 0; i < reader.FieldCount; i++)
                    {
                        fields[i] = reader.GetName(i);
                    }

                    if (returnType != typeof(object)) return true;
                    var instance = fields.ToDictionary(name => name,
                        name => reader[name] is DBNull ? null : reader[name]);

                    result = instance;
                }
                else if (returnType is { IsPrimitive: true } || 
                         Nullable.GetUnderlyingType(returnType) is {IsPrimitive: true} || 
                         returnType == typeof(string))
                {
                    result = command.ExecuteScalar();
                }
                else if (returnType.GetInterface(nameof(IEnumerable<object>)) != null)
                {
                    _reader = command.ExecuteReader();
                    if (returnType.IsGenericType && returnType.GetGenericArguments()[0] != typeof(object))
                    {
                        if (returnType.GetGenericArguments()[0] == typeof(Dictionary<string, object>))
                        {
                            result = Utils.GetIteratorDictionary(_reader, _configuration);
                        }
                        else
                        {
                            var type = returnType.GetGenericArguments()[0];
                            result = typeof(Utils).GetMethod(nameof(Utils.GetIterator),
                                    BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type)
                                .Invoke(null, [_reader, _configuration]);
                        }
                    }
                    else
                    {
                        result = Utils.GetIteratorDynamic(_reader, _configuration);
                    }
                }
                else if (returnType == typeof(object))
                {
                    using (_reader = command.ExecuteReader())
                    {
                        if (!_reader.Read()) return true;
                        var fields = new string[_reader.FieldCount];
                        for (i = 0; i < _reader.FieldCount; i++)
                        {
                            fields[i] = _reader.GetName(i) is { Length: > 0 } name ? name : Utils.ReturnValue;
                        }

                        if (returnType == typeof(object))
                        {
                            var builder = new DynamicTypeBuilder(Utils.AnonymousTypePrefix + _reader.GetHashCode());
                            foreach (var name in fields)
                            {
                                builder.AddProperty(name, _reader.GetFieldType(_reader.GetOrdinal(name)));
                            }

                            var type = builder.CreateType();
                            var instance = Activator.CreateInstance(type);
                            foreach (var name in fields)
                            {
                                type.GetProperty(name)?.SetValue(instance,
                                    _reader[name] is DBNull ? null : _reader[name], null);
                            }

                            result = instance;
                        }
                        else
                        {
                            var instance = Activator.CreateInstance(returnType);
                            foreach (var prop in returnType.GetProperties())
                            {
                                if (Array.IndexOf(fields, prop.Name) != -1)
                                {
                                    prop.SetValue(instance,
                                        _reader[prop.Name] is DBNull ? null : _reader[prop.Name], null);
                                }
                            }

                            result = instance;
                        }
                    }
                }
            }
            else
            {
                if (_configuration.ProviderFactory.CreateParameter() is not { } returnValue) return false;
                returnValue.Direction = ParameterDirection.ReturnValue;
                returnValue.ParameterName = Utils.ReturnValue;
                returnValue.DbType = returnType.ToClrType(_configuration);
                command.Parameters.Add(returnValue);
                command.ExecuteNonQuery();

                result = returnValue.Value;
            }
        }
        else
        {
            command.ExecuteNonQuery();
        }

        return true;
    }
}