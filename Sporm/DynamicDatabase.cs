namespace Sporm;

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Dynamic;
using System.Reflection;

/// <summary>
/// This is a dynamic class representing a pseud database which its stored procedures is callable via method names.
/// </summary>
public class DynamicDatabase : DynamicObject
{
    private readonly DbConnection _connection = null!;
    private readonly Configuration _configuration;
    private readonly ResultExtractor? _extractor;

    public DynamicDatabase(Configuration configuration)
    {
        if (configuration.ProviderFactory.CreateConnection() is not { } connection) return;
        _connection = connection;
        _connection.ConnectionString = configuration.ConnectionString;
        _configuration = configuration;
        _extractor = new ResultExtractor(_configuration);
    }
    
    private void CreateParameters(DbCommand command, CallInfo callInfo, IReadOnlyList<object?>? args)
    {
        var i = 0;
        if (callInfo.ArgumentNames.Count == 0)
        {
            var builder = _configuration.ProviderFactory.CreateCommandBuilder();
            builder?.GetType().GetMethod(Utils.DeriveParameters)?.Invoke(null, [command]);

            var j = 0;
            for (i = 0; i < callInfo.ArgumentCount; i++)
            {
                while (command.Parameters[i + j].Direction != ParameterDirection.Input
                       || command.Parameters.Count == i + j) j++;
                command.Parameters[i + j].Value = args![i];
            }
        }
        else
        {
            foreach (var item in callInfo.ArgumentNames)
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

        var methodName = binder.Name;
        var returnAsResult = methodName.EndsWith('_');
        if (methodName.EndsWith('_'))
        {
            methodName = methodName[..^1];
        }

        var isAsync = methodName.EndsWith(Utils.AsyncMethodPostfix);
        

        if (_configuration.ProviderFactory.CreateCommand() is not { } command) return false;
        command.Connection = _connection;
        command.CommandType = CommandType.StoredProcedure;
        command.CommandText = isAsync ? methodName[..^5] : methodName;
        if (_configuration.Inflector != null)
            command.CommandText = _configuration.Inflector(command.CommandText);


        var genericTypeArguments = binder.GetGenericTypeArguments();
        var returnType = genericTypeArguments?.Count > 0
            ? genericTypeArguments[0]
            : isAsync
                ? typeof(void)
                : typeof(object);

        if (returnType != typeof(void))
        {
            if (!returnAsResult)
            {
                if (isAsync)
                {
                    CreateParameters(command, binder.CallInfo, args);
                    result = returnType == typeof(void)
                        ? ExecuteNonQueryAsync(command)
                        : _extractor?.Extract(command, typeof(Task<>).MakeGenericType(returnType));
                }
                else
                {
                    _connection.Open();
                    CreateParameters(command, binder.CallInfo, args);
                    result = _extractor?.Extract(command, returnType);
                }
            }
            else
            {
                CreateParameters(command, binder.CallInfo, args);
                if (_configuration.ProviderFactory.CreateParameter() is not { } returnValue) return false;
                returnValue.Direction = ParameterDirection.ReturnValue;
                returnValue.ParameterName = Utils.ReturnValue;
                returnValue.DbType = returnType.ToClrType(_configuration);
                command.Parameters.Add(returnValue);

                if (isAsync)
                {
                    result = typeof(DynamicDatabase)
                        .GetMethod(nameof(ExecuteReturnValueAsync), BindingFlags.Static | BindingFlags.NonPublic)!
                        .MakeGenericMethod(returnType)
                        .Invoke(null, [command, returnValue]);
                }
                else
                {
                    command.ExecuteNonQuery();
                    result = ConvertReturnValue(returnValue.Value, returnType);
                }
            }
        }
        else
        {
            result = isAsync ? ExecuteNonQueryAsync(command) : command.ExecuteNonQuery();
        }

        return true;
    }

    private static async Task ExecuteNonQueryAsync(DbCommand command)
    {
        if (command.Connection?.State != ConnectionState.Open)
            await command.Connection!.OpenAsync();

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ExecuteReturnValueAsync<T>(DbCommand command, DbParameter returnValue)
    {
        if (command.Connection?.State != ConnectionState.Open)
            await command.Connection!.OpenAsync();

        await command.ExecuteNonQueryAsync();
        return (T?)ConvertReturnValue(returnValue.Value, typeof(T));
    }

    private static object? ConvertReturnValue(object? value, Type returnType)
    {
        if (value is null or DBNull)
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;

        return Convert.ChangeType(value, returnType);
    }
}
