using System.Reflection;

namespace Sporm
{
	using System;
	using System.Text.RegularExpressions;

	/// <summary>
	/// says that the target stored procedure returns the result as 'Return Value' parameter.
	/// </summary>
	[AttributeUsage(AttributeTargets.Method)]
	public class ReturnValueAsResultAttribute : Attribute;

	/// <summary>
	/// says that this method parameter should be considered as target stored procedure 'Return Value' parameter.
	/// </summary>
	[AttributeUsage(AttributeTargets.Parameter)]
	public class ReturnValueAttribute : Attribute;

    /// <summary>
    /// says that this method parameter should be considered as target stored procedure 'Size' parameter.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class SizeAttribute(int size) : Attribute
    {
	    public int Size { get;} = size;

	    public static int GetSizeOrDefault(ParameterInfo param) =>
		    (GetCustomAttribute(param, typeof(SizeAttribute)) as SizeAttribute)!.Size;
	    
    }
	
	/// <summary>
	/// This attributes is used when you want to name the attribute something other than the database name.
	/// </summary>
	[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method)]
	public class DbNameAttribute : Attribute
	{
		/// <summary>
		/// Initializes a new instance of the attribute.
		/// </summary>
		/// <param name="name"></param>
		public DbNameAttribute(string name)
		{
			if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, @"^\w*$"))
				throw new ArgumentException("The name must be an valid alphanumeric variable name.");
			Name = name;
		}

		public static string GetNameOrDefault(MemberInfo member) =>
			IsDefined(member, typeof(DbNameAttribute))
				? (GetCustomAttribute(member, typeof(DbNameAttribute)) as DbNameAttribute)?.Name ?? member.Name
				: member.Name;

		
		public static string? GetNameOrDefault(ParameterInfo param) =>
			IsDefined(param, typeof(DbNameAttribute))
				? (GetCustomAttribute(param, typeof(DbNameAttribute)) as DbNameAttribute)?.Name ?? param.Name
				: param.Name;

		/// <summary>
		/// The parameter name in the database stored procedure syntax.
		/// </summary>
		private string Name { get;}
	}

}
