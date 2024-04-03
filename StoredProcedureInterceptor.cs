using Castle.DynamicProxy;

namespace Sporm;

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;

/// <summary>
/// The brain of the project: Windsor IoC Interceptor.
/// </summary>
public class StoredProcedureInterceptor(Configuration configuration) : IInterceptor
{
    private DbConnection _connection = null!;
    private readonly ResultExtractor _extractor = new(configuration);


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
            
            var returnType = invocation.Method.ReturnType;

            if (invocation.Method.DeclaringType == typeof(IAsyncDisposable))
            {
                invocation.ReturnValue = _connection.DisposeAsync();
                return;
            }

            if (configuration.ProviderFactory.CreateConnection() is not { } connection) return;
            _connection = connection;
            _connection.ConnectionString = configuration.ConnectionString;

            if(!returnType.IsTaskResult() && !returnType.IsAsyncVoid() && !returnType.IsAsyncEnumerable()) 
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
            

            if (returnType.IsVoid())
            {
                command.ExecuteNonQuery();
            }
            else if (returnType.IsAsyncVoid())
            {
                async Task Func()
                {
                    await command.Connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                }

                invocation.ReturnValue = Func();
            }
            else
            {
                if (!returnAsResult)
                {
                    invocation.ReturnValue = _extractor.Extract(command, returnType);
                }
                else
                {
                    if (configuration.ProviderFactory.CreateParameter() is not { } returnValue) return;
                    returnValue.Direction = ParameterDirection.ReturnValue;
                    returnValue.ParameterName = Utils.ReturnValue;
                    returnValue.DbType = returnType.ToClrType(configuration);
                    command.Parameters.Add(returnValue);

                    if (returnType.IsTaskResult())
                    {
                        async Task<object?> Func()
                        {
                            await command.Connection.OpenAsync();
                            await command.ExecuteNonQueryAsync();
                            return Convert.ChangeType(returnValue.Value, returnType);
                        }

                        invocation.ReturnValue = Func();
                    }
                    else
                    {
                        command.ExecuteNonQuery();
                        invocation.ReturnValue = Convert.ChangeType(returnValue.Value, returnType);
                    }
                }
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
            if (invocation.Method.DeclaringType == typeof(IDisposable) && _connection.State == ConnectionState.Open)
                _connection.Dispose();
        }
    }
}