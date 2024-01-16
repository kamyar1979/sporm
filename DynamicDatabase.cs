
namespace Sporm
{
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
        private readonly DbProviderFactory _factory = null!;
        private readonly DbConnection _connection = null!;
        private DbDataReader _reader = null!;
        
        public DynamicDatabase(DatabaseProvider provider)
        {
            var factory = DbProviderFactories.GetFactory(provider.ProviderName);
            if (factory.CreateConnection() is not { } connection) return;
            _connection = connection;
            _connection.ConnectionString = provider.ConnectionString;
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
                methodName = methodName.Substring(0, methodName.Length - 1);
            }

            if(_factory.CreateCommand() is not {}  command) return false;
            command.Connection = _connection;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = methodName;

            var i = 0;

            if (binder.CallInfo.ArgumentNames.Count == 0)
            {
                var builder = _factory.CreateCommandBuilder();
                builder?.GetType().GetMethod("DeriveParameters")?.Invoke(null, [command]);

                for (i = 0; i < binder.CallInfo.ArgumentCount; i++)
                {
                    command.Parameters[i + 1].Value = args![i];
                }
            }
            else
            {
                foreach (var item in binder.CallInfo.ArgumentNames)
                {
                    if(_factory.CreateParameter() is not {} param) continue;
                    param.ParameterName = item;
                    param.Direction = ParameterDirection.Input;
                    param.DbType = (DbType)Enum.Parse(typeof(DbType), args![i]!.GetType().Name);
                    param.Value = args[i];
                    command.Parameters.Add(param);
                    i++;
                }
            }

            var returnType = binder.GetGenericTypeArguments()?.Count > 0 ? binder.GetGenericTypeArguments()?[0]
                : typeof(object);

            if (returnType != typeof(void))
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
                        var instance = fields.ToDictionary(name => name, name => reader[name] is DBNull ? null : reader[name]);

                        result = instance;
                    }
                    else if (returnType is { IsPrimitive: true } || returnType == typeof(string))
                    {
                        result = command.ExecuteScalar();
                    }
                    else if (returnType?.GetInterface("IEnumerable") != null)
                    {
                        _reader = command.ExecuteReader();
                        if (returnType.IsGenericType && returnType.GetGenericArguments()[0] != typeof(object))
                        {
                            if (returnType.GetGenericArguments()[0] == typeof(Dictionary<string, object>))
                            {
                                result = Utils.GetIteratorDictionary(_reader);
                            }
                            else
                            {
                                var type = returnType.GetGenericArguments()[0];
                                result = GetType().GetMethod("GetIterator")?.MakeGenericMethod(type)
                                    .Invoke(this, new object[] { _reader });
                            }
                        }
                        else
                        {
                            result = Utils.GetIteratorDynamic(_reader);
                        }
                    }
                    else if (returnType == typeof(object))
                    {
                        using (this._reader = command.ExecuteReader())
                        {
                            if (_reader.Read())
                            {
                                string[] fields = new string[_reader.FieldCount];
                                for (i = 0; i < _reader.FieldCount; i++)
                                {
                                    fields[i] = _reader.GetName(i);
                                }

                                if (returnType == typeof(object))
                                {
                                    var builder = new DynamicTypeBuilder("anonymous_" + _reader.GetHashCode());
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
                }
                else
                {
                    if(_factory.CreateParameter() is not {} returnValue) return false;
                    returnValue.Direction = ParameterDirection.ReturnValue;
                    returnValue.ParameterName = "RetVal";
                    if(returnType != null)
                        returnValue.DbType = (DbType)Enum.Parse(typeof(DbType), returnType.Name);
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
}