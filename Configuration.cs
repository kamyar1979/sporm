using System.Data;
using System.Data.Common;
using System.Reflection;

namespace Sporm;

public record struct Configuration(
    string ConnectionString,
    DbProviderFactory ProviderFactory,
    Func<string, string>? Inflector,
    Func<MemberInfo, DbType>? TypeResolver,
    bool? NoReturnValue = false);

public class ConfigurationBuilder
{
    private Configuration _configuration;

    private ConfigurationBuilder(Configuration configuration)
    {
        _configuration = configuration;
    }

    public static ConfigurationBuilder ForDatabase(string connectionString, DbProviderFactory factory)
    {
        return new ConfigurationBuilder(
            new Configuration(
                connectionString,
                factory,
                null, null
            ));
    }

    public Configuration Build()
    {
        return _configuration;
    }

    public ConfigurationBuilder Inflector(Func<string, string> inflector)
    {
        _configuration.Inflector = inflector;
        return this;
    }

    public ConfigurationBuilder TypeResolver(Func<MemberInfo, DbType> typeResolver)
    {
        _configuration.TypeResolver = typeResolver;
        return this;
    }

    public ConfigurationBuilder IgnoreReturnValue()
    {
        _configuration.NoReturnValue = true;
        return this;
    }
}