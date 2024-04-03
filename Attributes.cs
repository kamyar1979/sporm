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
	    private int Size { get;} = size;

	    public static int GetSizeOrDefault(ParameterInfo param) =>
		    (GetCustomAttribute(param, typeof(SizeAttribute)) as SizeAttribute)!.Size;
	    
    }
	
	/// <summary>
	/// This attributes is used when you want to name the attribute something other than the database name.
	/// </summary>
	[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Method | AttributeTargets.Property)]
	public class DbNameAttribute : Attribute
	{
		/// <summary>
		/// Initializes a new instance of the attribute.
		/// </summary>
		/// <param name="name"></param>
		public DbNameAttribute(string name)
		{
			if (string.IsNullOrEmpty(name) || !Regex.IsMatch(name, Utils.ValidNamePattern))
				throw new ArgumentException("The name must be an valid alphanumeric variable name.");
			Name = name;
		}

		public static bool TryGetName(MemberInfo member, out string name)
		{
			if ((GetCustomAttribute(member, typeof(DbNameAttribute)) as DbNameAttribute)?.Name is { } dbName)
			{
				name = dbName;
				return true;
			}
			name = member is MethodInfo method && 
				method.Name.EndsWith(Utils.AsyncMethodPostfix)
				? method.Name[..^5]
				: member.Name;
			return false;
		}

		public static bool TryGetName(ParameterInfo param, out string? name)
		{
			if ((GetCustomAttribute(param, typeof(DbNameAttribute)) as DbNameAttribute)?.Name is { } dbName)
			{
				name = dbName;
				return true;
			}

			name = param.Name;
			return false;
		}
		
		public static bool TryGetName(PropertyInfo propertyInfo, out string? name)
		{
			if ((GetCustomAttribute(propertyInfo, typeof(DbNameAttribute)) as DbNameAttribute)?.Name is { } dbName)
			{
				name = dbName;
				return true;
			}

			name = propertyInfo.Name;
			return false;
		}

		/// <summary>
		/// The parameter name in the database stored procedure syntax.
		/// </summary>
		private string Name { get;}
	}

}
