namespace Sporm
{
	using System;
	using System.Collections.Generic;
	using System.Dynamic;
	using System.Linq.Expressions;
	using System.Reflection;

	// <summary>Framework detection and specific implementations.</summary>
	internal static class FrameworkTools
	{
		private static readonly Func<InvokeMemberBinder, IList<Type>>? FrameworkTypeArgumentsGetter;

		/// <summary>Gets a value indicating whether application is running under mono runtime.</summary>
		private static bool IsMono { get; } = Type.GetType("Mono.Runtime") != null;

		static FrameworkTools()
		{
			FrameworkTypeArgumentsGetter = CreateTypeArgumentsGetter();
		}

		private static Func<InvokeMemberBinder, IList<Type>>? CreateTypeArgumentsGetter()
		{
			if (IsMono)
			{
				var binderType = typeof(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException).Assembly.GetType("Microsoft.CSharp.RuntimeBinder.CSharpInvokeMemberBinder");

				if (binderType == null) return null;
				ParameterExpression param = Expression.Parameter(typeof(InvokeMemberBinder), "o");

				return Expression.Lambda<Func<InvokeMemberBinder, IList<Type>>>(
					Expression.TypeAs(
						Expression.Field(
							Expression.TypeAs(param, binderType), "typeArguments"),
						typeof(IList<Type>)), param).Compile();
			}
			else
			{
				var inter = typeof(Microsoft.CSharp.RuntimeBinder.RuntimeBinderException).Assembly.GetType("Microsoft.CSharp.RuntimeBinder.ICSharpInvokeOrInvokeMemberBinder");

				if(inter?.GetProperty("TypeArguments") is not { CanRead: true} prop ) return null;

				var objParam = Expression.Parameter(typeof(InvokeMemberBinder), "o");

				return Expression.Lambda<Func<InvokeMemberBinder, IList<Type>>>(
					Expression.TypeAs(
						Expression.Property(
							Expression.TypeAs(objParam, inter),
							prop.Name),
						typeof(IList<Type>)), objParam).Compile();
			}
		}

		/// <summary>Extension method allowing to easily extract generic type arguments from <see cref="InvokeMemberBinder"/>.</summary>
		/// <param name="binder">Binder from which get type arguments.</param>
		/// <returns>List of types passed as generic parameters.</returns>
		public static IList<Type>? GetGenericTypeArguments(this InvokeMemberBinder binder)
		{
			// First try to use delegate if exist
			if (FrameworkTypeArgumentsGetter != null)
				return FrameworkTypeArgumentsGetter(binder);

			if (IsMono)
			{
				// In mono this is trivial.

				// First we get field info.
				var field = binder.GetType().GetField("typeArguments", BindingFlags.Instance |
					BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

				// If this was a success get and return it's value
				if (field != null)
					return field.GetValue(binder) as IList<Type>;
			}
			else
			{
				// In this case, we need more aerobic :D

				// First, get the interface
				var inter = binder.GetType().GetInterface("Microsoft.CSharp.RuntimeBinder.ICSharpInvokeOrInvokeMemberBinder");

				if (inter != null)
				{
					// Now get property.
					var prop = inter.GetProperty("TypeArguments");

					// If we have a property, return it's value
					if (prop != null)
						return prop.GetValue(binder, null) as IList<Type>;
				}
			}

			// Sadly return null if failed.
			return null;
		}
	}
}
