namespace Sporm;

using System;
using System.Collections.Generic;
using Castle.DynamicProxy;

/// <summary>
/// This class is the only thing you have to consider using: Sets up the whole things!
/// </summary>
public static class Setup
{
    private static readonly StoredProcedureInterceptor? Interceptor;
    private static readonly Dictionary<Type, Configuration> DatabaseConfigurations = new();

    static Setup()
    {
        Interceptor = new StoredProcedureInterceptor(DatabaseConfigurations);
    }

    /// <summary>
    /// Registers an interface type and binds it to a connection string name in teh configuration file.
    /// </summary>
    /// <typeparam name="T">Interface type</typeparam>
    public static void Register<T>(
        Configuration configuration) where T : class
    {
        DatabaseConfigurations[typeof(T)] = configuration;
    }

    /// <summary>
    /// Creates an instance of the DB provider to use the stored procedures.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static T GetInstance<T>() where T : class
    {
        var proxyGen = new ProxyGenerator();
        return proxyGen.CreateInterfaceProxyWithoutTarget<T>(Interceptor);
    }

    /// <summary>
    /// Creates a dynamic instance of the DB provider to use the stored procedures.
    /// </summary>
    /// <returns></returns>
    public static object GetInstance(Configuration configuration)
    {
        return new DynamicDatabase(configuration);
    }
}