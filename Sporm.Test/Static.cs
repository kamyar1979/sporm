using System.Globalization;
using Inflector;
using TestSporm;

namespace Sporm.Test;




public class Static
{
    [Fact]
    public async void SimpleTypes()
    {
        Inflector.Inflector.SetDefaultCultureFunc = () => CultureInfo.CurrentUICulture;

        AppContext.SetSwitch("Npgsql.EnableStoredProcedureCompatMode", true);

        var conf = ConfigurationBuilder.ForDatabase("server=localhost;user id=kamyar;password=Nautilus;database=zeero",
            Npgsql.NpgsqlFactory.Instance).Inflector(s => s.Underscore()).Deflector(s => s.Pascalize());

        await using var db = conf.CreateInstance<IMyDb>();
        

        var result = await db.GetUsersAsync();
        
        await foreach (var item in result)
        {
            Assert.Equal(item.Name, "Kamyar Inanloo");
        }
    }
}