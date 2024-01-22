using Castle.DynamicProxy;

namespace Sporm
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Reflection;

    /// <summary>
    /// The brain of the project: Windsor IoC Interceptor.
    /// </summary>
    public class StoredProcedureInterceptor(IReadOnlyDictionary<Type, DatabaseProvider> providers) : IInterceptor
    {
        private DbConnection _connection = null!;
        private DbProviderFactory _factory = null!;
        private DbDataReader _reader = null!;

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

                if (!providers.TryGetValue(invocation.Method.DeclaringType!, out var provider)) return;
                _factory = DbProviderFactories.GetFactory(provider.ProviderName);
                if (_factory.CreateConnection() is not { } connection) return;
                _connection = connection;
                _connection.ConnectionString = provider.ConnectionString;

                _connection.Open();
                if (_factory.CreateCommand() is not { } command) return;

                command.Connection = _connection;
                command.CommandType = CommandType.StoredProcedure;
                command.CommandText = DbNameAttribute.GetNameOrDefault(invocation.Method);
                if (provider.inflector != null)
                    command.CommandText = provider.inflector(command.CommandText);

                var returnAsResult = Attribute.IsDefined(invocation.Method, typeof(ReturnValueAsResultAttribute));

                var i = 0;
                foreach (var item in invocation.Method.GetParameters())
                {
                    if (item.IsOptional && invocation.Arguments.Length < i + 1)
                    {
                        break;
                    }

                    //Check Nullable parameter. 
                    if (invocation.Arguments[i] == null &&
                        item.ParameterType.IsGenericType &&
                        item.ParameterType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        //Jump if ParameterValue == null && ParameterType is Nullable  
                    }
                    else
                    {
                        if (_factory.CreateParameter() is not { } param) return;
                        param.ParameterName = DbNameAttribute.GetNameOrDefault(item);
                        if (provider.inflector != null)
                            param.ParameterName = provider.inflector(param.ParameterName);

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
                        param.DbType = (DbType)Enum.Parse(typeof(DbType), returnType.Name.Replace("&", ""));

                        if (Attribute.IsDefined(item, typeof(SizeAttribute)))
                        {
                            param.Size = SizeAttribute.GetSizeOrDefault(item);
                        }

                        param.Value = invocation.Arguments[i];
                        command.Parameters.Add(param);
                    }

                    i++;
                }

                if (invocation.Method.ReturnType != typeof(void))
                {
                    if (!returnAsResult)
                    {
                        if (invocation.Method.ReturnType == typeof(Dictionary<string, object>))
                        {
                            _reader = command.ExecuteReader();
                            if (_reader.Read())
                            {
                                var fields = new string[_reader.FieldCount];
                                for (i = 0; i < _reader.FieldCount; i++)
                                {
                                    fields[i] = _reader.GetName(i);
                                }

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
                                            .Invoke(null, [_reader]);
                                }
                            }
                            else
                            {
                                invocation.ReturnValue = Utils.GetIteratorDynamic(_reader);
                            }
                        }
                        else if (invocation.Method.ReturnType == typeof(object))
                        {
                            using (_reader = command.ExecuteReader())
                            {
                                if (_reader.Read())
                                {
                                    string[] fields = new string[_reader.FieldCount];
                                    for (i = 0; i < _reader.FieldCount; i++)
                                    {
                                        fields[i] = _reader.GetName(i);
                                    }

                                    if (invocation.Method.ReturnType == typeof(object))
                                    {
                                        var builder = new DynamicTypeBuilder("anonym_" + _reader.GetHashCode());
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
                        if (_factory.CreateParameter() is not { } returnValue) return;
                        returnValue.Direction = ParameterDirection.ReturnValue;
                        returnValue.ParameterName = "RetVal";
                        returnValue.DbType = (DbType)Enum.Parse(typeof(DbType),
                            invocation.Method.ReturnType.Name.Replace("&", ""));
                        command.Parameters.Add(returnValue);

                        command.ExecuteNonQuery();

                        invocation.ReturnValue = Convert.ChangeType(returnValue.Value, invocation.Method.ReturnType);
                    }
                }
                else
                {
                    command.ExecuteNonQuery();
                }

                i = 0;
                foreach (var item in invocation.Method.GetParameters())
                {
                    if (item.IsOut)
                    {
                        invocation.Arguments[i] = command.Parameters[i].Value;
                    }

                    i++;
                }
            }
            finally
            {
                if (invocation.Method.DeclaringType!.GetInterface("IDisposable") == null) _connection.Dispose();
            }
        }
    }
}