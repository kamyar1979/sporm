namespace Sporm
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Reflection.Emit;

    /// <summary>
    /// This class creates a dynamic type on-the-fly.
    /// </summary>
    public class DynamicTypeBuilder
    {
        private readonly TypeBuilder _typeBuilder;

        /// <summary>
        /// Creates a new type using the name.
        /// </summary>
        /// <param name="typeName">The name of the type, containing the possible namespace.</param>
        public DynamicTypeBuilder(string typeName)
        {
            var assemblyBuilder =
                AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OnTheFly"), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("AnonymousTypes");
            _typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public);
        }

        /// <summary>
        /// Creates a new type using the name and the module name.
        /// </summary>
        /// <param name="typeName">The name of the type, containing the possible namespace.</param>
        /// <param name="moduleName">The name fo the module.</param>
        public DynamicTypeBuilder(string typeName, string moduleName)
        {
            var assemblyBuilder =
                AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("OnTheFly"), AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule(moduleName);
            _typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public);
        }

        /// <summary>
        /// Creates a new instance of this class using a sample dictionary, containing the properties and their values.
        /// </summary>
        /// <param name="dict">The Dictionary contains the property names as key, and their values as values.</param>
        /// <returns>An instance of this class.</returns>
        public static DynamicTypeBuilder FromDictionary(IDictionary<string, object> dict)
        {
            var name = "anonym_" + dict.GetHashCode().ToString();
            var result = new DynamicTypeBuilder(name);
            foreach (var item in dict)
            {
                result.AddProperty(item.Key, item.Value.GetType());
            }

            return result;
        }

        /// <summary>
        /// Adds a property with Type and name.
        /// </summary>
        /// <typeparam name="T">The type of the property.</typeparam>
        /// <param name="name">Name of the property.</param>
        /// <returns>Returns the propertyInfo object for possible using.</returns>
        public PropertyInfo AddProperty<T>(string name)
        {
            return AddProperty(name, typeof(T));
        }

        /// <summary>
        /// Adds a property with the type and name.
        /// </summary>
        /// <param name="name">Name of the property</param>
        /// <param name="type">Type of the property.</param>
        /// <returns>Returns the propertyInfo object for possible using.</returns>
        public PropertyInfo AddProperty(string name, Type type)
        {
            var fieldBuilder = _typeBuilder.DefineField('_' + name.ToLower(), type,
                FieldAttributes.Private | FieldAttributes.HasDefault);
            var propertyBuilder = _typeBuilder.DefineProperty(name, PropertyAttributes.None, type, Type.EmptyTypes);
            var methodGetBuilder = _typeBuilder.DefineMethod("get_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, type,
                Type.EmptyTypes);
            var methodSetBuilder = _typeBuilder.DefineMethod("set_" + name,
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, null,
                [type]);

            var getIl = methodGetBuilder.GetILGenerator();

            getIl.Emit(OpCodes.Ldarg_0);
            getIl.Emit(OpCodes.Ldfld, fieldBuilder);
            getIl.Emit(OpCodes.Ret);

            var setIl = methodSetBuilder.GetILGenerator();

            setIl.Emit(OpCodes.Ldarg_0);
            setIl.Emit(OpCodes.Ldarg_1);
            setIl.Emit(OpCodes.Stfld, fieldBuilder);
            setIl.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(methodGetBuilder);
            propertyBuilder.SetSetMethod(methodSetBuilder);

            return propertyBuilder;
        }

        /// <summary>
        /// Returns the resulting type.
        /// </summary>
        /// <remarks>
        ///  Note that you can not add any further properties, due to C# limitations.That is, you can not call AddProperty methods after calling 
        /// </remarks>
        /// <returns>Returns the Type as variable.</returns>
        public Type CreateType()
        {
            return _typeBuilder.CreateType();
        }
    }
}