namespace Sporm
{
	using System;
	using System.Collections.Generic;
	using System.Data;
	using System.Data.Common;

	public record struct DatabaseProvider(string ConnectionString, string ProviderName);
	internal static class Utils
	{
		internal static IEnumerable<Dictionary<string, object?>> GetIteratorDictionary(DbDataReader reader)
		{
			var fields = new string[reader.FieldCount];
			for (var i = 0; i < reader.FieldCount; i++)
			{
				fields[i] = reader.GetName(i);
			}
			while (reader.Read())
			{
				yield return fields.ToDictionary(name => name, name => reader[name] is DBNull ? null : reader[name]);
			}
			reader.Close();
		}

		internal static IEnumerable<T> GetIterator<T>(IDataReader reader) where T : class, new()
		{
			var fields = new string[reader.FieldCount];
			for (var i = 0; i < reader.FieldCount; i++)
			{
				fields[i] = reader.GetName(i);
			}
			while (reader.Read())
			{
				var instance = new T();
				foreach (var prop in typeof(T).GetProperties())
				{
					if (Array.IndexOf(fields, prop.Name) != -1)
					{
						prop.SetValue(instance, reader[prop.Name] is DBNull ? null : reader[prop.Name], null);
					}
				}
				yield return instance;
			}
			reader.Close();
		}

		internal static IEnumerable<object?> GetIteratorDynamic(IDataReader reader)
		{
			var builder = new DynamicTypeBuilder("anonym_" + reader.GetHashCode());
			var fields = new string[reader.FieldCount];
			for (var i = 0; i < reader.FieldCount; i++)
			{
				fields[i] = reader.GetName(i);
			}
			foreach (var name in fields)
			{
				builder.AddProperty(name, reader.GetFieldType(reader.GetOrdinal(name)));
			}
			var type = builder.CreateType();
			while (reader.Read())
			{
				var instance = Activator.CreateInstance(type);
				foreach (var name in fields)
				{
					type.GetProperty(name)?.SetValue(instance, reader[name] is DBNull ? null : reader[name], null);
				}
				yield return instance;
			}
			reader.Close();
		}

	}
}
