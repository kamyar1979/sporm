using System.Globalization;
using Inflector;
using TestSporm;

namespace Sporm.Test;




public class Static
{
    [Fact]
    public async Task SimpleTypes()
    {
        var connectionString = Environment.GetEnvironmentVariable("SPORM_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        Inflector.Inflector.SetDefaultCultureFunc = () => CultureInfo.CurrentUICulture;

        AppContext.SetSwitch("Npgsql.EnableStoredProcedureCompatMode", true);

        var conf = ConfigurationBuilder.ForDatabase(connectionString,
            Npgsql.NpgsqlFactory.Instance).Inflector(s => s.Underscore()).Deflector(s => s.Pascalize());

        await using var db = conf.CreateInstance<IMyDb>();
        

        var result = await db.GetUsersAsync();
        
        await foreach (var item in result)
        {
            Assert.Equal("Kamyar Inanloo", item.Name);
        }
    }
}
