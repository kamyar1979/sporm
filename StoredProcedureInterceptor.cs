using Castle.DynamicProxy;

namespace Sporm;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;

/// <summary>
/// The brain of the project: Windsor IoC Interceptor.
/// </summary>
public class StoredProcedureInterceptor(Configuration configuration) : IInterceptor
{
    private DbConnection _connection = null!;
    private DbDataReader _reader = null!;


    private IEnumerable<DbParameter> CreateParameters(IInvocation invocation)
    {
        foreach (var (item, index)in invocation.Method.GetParameters().Select((p, i) => (p, i)))
        {
            if (item.IsOptional && invocation.Arguments.Length < index + 1)
            {
                break;
            }

            //Check Nullable parameter. 
            if (invocation.Arguments[index] == null &&
                item.ParameterType.IsGenericType &&
                item.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                continue;
            }

            if (configuration.ProviderFactory.CreateParameter() is not { } param) yield break;
            if (!DbNameAttribute.TryGetName(item, out var dbParamName))
            {
                if (configuration.Inflector != null && dbParamName != null)
                    dbParamName = configuration.Inflector(dbParamName);
            }

            param.ParameterName = dbParamName;

            if (Attribute.IsDefined(item, typeof(ReturnValueAttribute)))
            {
                param.Direction = ParameterDirection.ReturnValue;
            }
            else
            {
                param.Direction = item.IsOut ? ParameterDirection.Output : ParameterDirection.Input;
            }

            //Check Nullable parameter && ParamValue != null
            var type = item.ParameterType;
            var underlyingType = Nullable.GetUnderlyingType(type);
            var returnType = underlyingType ?? type;
            param.DbType = returnType.ToClrType(configuration);

            if (Attribute.IsDefined(item, typeof(SizeAttribute)))
            {
                param.Size = SizeAttribute.GetSizeOrDefault(item);
            }

            param.Value = invocation.Arguments[index];
            yield return param;
        }
    }

    /// <summary>
    /// Main Interceptor method.
    /// </summary>
    /// <param name="invocation"></param>
    public void Intercept(IInvocation invocation)
    {
        try
        {
            if (invocation.Method.DeclaringType == typeof(IDisposable))
            {
                _connection.Dispose();
                return;
            }

            if (configuration.ProviderFactory.CreateConnection() is not { } connection) return;
            _connection = connection;
            _connection.ConnectionString = configuration.ConnectionString;

            _connection.Open();
            if (configuration.ProviderFactory.CreateCommand() is not { } command) return;

            command.Connection = _connection;
            command.CommandType = CommandType.StoredProcedure;
            if (!DbNameAttribute.TryGetName(invocation.Method, out var dbName))
            {
                if (configuration.Inflector != null)
                    dbName = configuration.Inflector(dbName);
            }

            command.CommandText = dbName;

            var returnAsResult = configuration.NoReturnValue != true &&
                                 Attribute.IsDefined(invocation.Method, typeof(ReturnValueAsResultAttribute));

            command.Parameters.AddRange(CreateParameters(invocation).ToArray());

            if (invocation.Method.ReturnType != typeof(void))
            {
                if (!returnAsResult)
                {
                    if (invocation.Method.ReturnType == typeof(Dictionary<string, object>))
                    {
                        _reader = command.ExecuteReader();
                        if (_reader.Read())
                        {
                            var fields = Enumerable.Range(0, _reader.FieldCount).Select(i => _reader.GetName(i))
                                .ToArray();

                            if (invocation.Method.ReturnType == typeof(object))
                            {
                                var instance = fields.ToDictionary(name => name,
                                    name => _reader[name] is DBNull ? null : _reader[name]);
                                invocation.ReturnValue = instance;
                            }
                        }
                    }
                    else if (invocation.Method.ReturnType.Namespace == nameof(System) &&
                             invocation.Method.ReturnType != typeof(object))
                    {
                        var result = command.ExecuteScalar();
                        invocation.ReturnValue = result is DBNull ? null : command.ExecuteScalar();
                    }
                    else if (invocation.Method.ReturnType.GetInterface(nameof(IEnumerable<object>)) != null)
                    {
                        _reader = command.ExecuteReader();
                        if (invocation.Method.ReturnType.IsGenericType &&
                            invocation.Method.ReturnType.GetGenericArguments()[0] != typeof(object))
                        {
                            if (invocation.Method.ReturnType.GetGenericArguments()[0] ==
                                typeof(Dictionary<string, object>))
                            {
                                invocation.ReturnValue = Utils.GetIteratorDictionary(_reader);
                            }
                            else
                            {
                                var type = invocation.Method.ReturnType.GetGenericArguments()[0];
                                invocation.ReturnValue =
                                    typeof(Utils).GetMethod(nameof(Utils.GetIterator),
                                            BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type)
                                        .Invoke(null, [_reader, configuration]);
                            }
                        }
                        else
                        {
                            invocation.ReturnValue = Utils.GetIteratorDynamic(_reader, configuration);
                        }
                    }
                    else if (invocation.Method.ReturnType == typeof(object))
                    {
                        using (_reader = command.ExecuteReader())
                        {
                            if (_reader.Read())
                            {
                                var fields = Enumerable.Range(0, _reader.FieldCount).Select(i => _reader.GetName(i))
                                    .ToArray();

                                if (invocation.Method.ReturnType == typeof(object))
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

                                    invocation.ReturnValue = instance;
                                }
                                else
                                {
                                    var instance = Activator.CreateInstance(invocation.Method.ReturnType);
                                    foreach (var prop in invocation.Method.ReturnType.GetProperties())
                                    {
                                        if (Array.IndexOf(fields, prop.Name) != -1)
                                        {
                                            prop.SetValue(instance,
                                                _reader[prop.Name] is DBNull ? null : _reader[prop.Name], null);
                                        }
                                    }

                                    invocation.ReturnValue = instance;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (configuration.ProviderFactory.CreateParameter() is not { } returnValue) return;
                    returnValue.Direction = ParameterDirection.ReturnValue;
                    returnValue.ParameterName = Utils.ReturnValue;
                    returnValue.DbType = invocation.Method.ReturnType.ToClrType(configuration);
                    command.Parameters.Add(returnValue);

                    command.ExecuteNonQuery();

                    invocation.ReturnValue = Convert.ChangeType(returnValue.Value, invocation.Method.ReturnType);
                }
            }
            else
            {
                command.ExecuteNonQuery();
            }

            foreach (var (item, index) in invocation.Method.GetParameters().Select((p, i) => (p, i)))
            {
                if (item.IsOut)
                {
                    invocation.Arguments[index] = command.Parameters[index].Value;
                }
            }
        }
        finally
        {
            if (invocation.Method.DeclaringType!.GetInterface(nameof(IDisposable)) == null) _connection.Dispose();
        }
    }
}