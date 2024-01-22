
namespace Sporm
{
    using System;
    using System.Collections.Generic;
    using Castle.DynamicProxy;

    /// <summary>
    /// This class is the only thing you have to consider using: Sets up the whole things!
    /// </summary>
    public static class Setup
    {
        private static readonly StoredProcedureInterceptor? Interceptor;
        private static readonly Dictionary<Type, DatabaseProvider> DatabaseProviders = new ();

        static Setup()
        {
            Interceptor = new StoredProcedureInterceptor(DatabaseProviders);
        }

        /// <summary>
        /// Registers an interface type and binds it to a connection string name in teh configuration file.
        /// </summary>
        /// <typeparam name="T">Interface type</typeparam>
        public static void Register<T>(
            string connectionString, 
            string providerName, 
            Func<string, string>? inflector = null) where T : class
        {
            DatabaseProviders[typeof(T)] = new DatabaseProvider(connectionString, providerName, inflector);
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
        /// <param name="provider"></param>
        /// <returns></returns>
        public static object GetInstance(DatabaseProvider provider)
        {
            return new DynamicDatabase(provider);
        }
    }
}