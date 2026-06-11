# Sporm

Sporm is a small stored procedure ORM for .NET: a spellbook for calling database
procedures and functions as ordinary C# methods.

Describe the shape of your database ritual in an interface, or summon it through
`dynamic`, and Sporm prepares the command, binds parameters, executes it, and
maps the result back into .NET values, objects, dictionaries, or streams.

It began as a charm for an older SQL Server database, but today it works with
any database provider that exposes a standard `DbProviderFactory`.

## What The Spell Does

Calling stored procedures by hand usually means repeating the same ADO.NET
ceremony:

- create the connection and command
- set `CommandType.StoredProcedure`
- bind input, output, and return-value parameters
- execute the command
- read scalars or rows
- map fields into models
- close the reader and connection

Sporm keeps the database boundary explicit, but removes most of the boilerplate.
Your C# method name becomes the procedure name. Your method parameters become
database parameters. Your return type tells Sporm how to read the result.

## Install The Grimoire

Install Sporm and the provider for your database:

```bash
dotnet add package Sporm
dotnet add package Npgsql
```

Sporm targets `.NET 8`.

Sporm does not choose your database driver for you. Use any provider that exposes
a `DbProviderFactory`, such as `NpgsqlFactory` for PostgreSQL.

## First Invocation: PostgreSQL

For PostgreSQL functions, enable Npgsql's stored procedure compatibility mode
before creating the Sporm configuration:

```csharp
AppContext.SetSwitch("Npgsql.EnableStoredProcedureCompatMode", true);
```

Create a database function:

```sql
CREATE FUNCTION add(a integer, b integer) RETURNS integer
LANGUAGE SQL
IMMUTABLE
RETURNS NULL ON NULL INPUT
RETURN a + b;
```

Declare the matching C# interface:

```csharp
public interface IMyDb : IDisposable
{
    int Add(int a, int b);
}
```

Bind the interface to the database and call the function:

```csharp
using Inflector;
using Npgsql;
using Sporm;

AppContext.SetSwitch("Npgsql.EnableStoredProcedureCompatMode", true);

var dbConfig = ConfigurationBuilder
    .ForDatabase(
        "Host=localhost;Username=postgres;Password=secret;Database=example",
        NpgsqlFactory.Instance)
    .Inflector(name => name.Underscore());

using var db = dbConfig.CreateInstance<IMyDb>();

var result = db.Add(6, 7);
Console.WriteLine(result); // 13
```

`IDisposable` matters for static interfaces. Use `using` so Sporm can dispose the
underlying connection when the invocation is complete.

## Reading Tables From The Cauldron

Sporm can map result sets into `IEnumerable<T>`.

```sql
CREATE TABLE IF NOT EXISTS users
(
    id bigint NOT NULL PRIMARY KEY,
    username varchar(60) NOT NULL UNIQUE,
    name varchar(256),
    email varchar(256),
    last_login timestamp with time zone
);

CREATE OR REPLACE FUNCTION get_users() RETURNS TABLE (
    id bigint,
    username varchar(60),
    name varchar(256),
    email varchar(256),
    last_login timestamp with time zone)
LANGUAGE PLPGSQL AS
$$
BEGIN
    RETURN QUERY
    SELECT users.id, users.username, users.name, users.email, users.last_login
    FROM users;
END
$$;
```

Define a model and a method:

```csharp
using Sporm;

public record struct User(
    long Id,
    [property: DbName("username")] string Username,
    string Name,
    string Email,
    DateTime LastLogin);

public interface IMyDb : IDisposable
{
    IEnumerable<User> GetUsers();
}
```

Then read rows like a normal method call:

```csharp
using var db = dbConfig.CreateInstance<IMyDb>();

var users = db.GetUsers().ToArray();
Console.WriteLine(users[0].Name);
```

`DbName` overrides the database name for a method, parameter, or property. In
the example above, it maps `Username` to the database column `username`.

## Async Incantations

Static interfaces can expose task-based methods:

```csharp
public interface IMyDb : IDisposable, IAsyncDisposable
{
    Task<int> AddAsync(int a, int b);
    Task SaveUserAsync(string username);
    Task<IAsyncEnumerable<User>> GetUsersAsync();
}
```

Use `await using` when your interface supports `IAsyncDisposable`:

```csharp
await using var db = dbConfig.CreateInstance<IMyDb>();

var result = await db.AddAsync(6, 7);
var users = await db.GetUsersAsync();

await foreach (var user in users)
{
    Console.WriteLine(user.Name);
}
```

Async method names automatically map without the `Async` suffix. With the
underscore inflector configured, `GetUsersAsync` calls `get_users`.

## Dynamic Summoning

If you do not want to write an interface, create a dynamic database object:

```csharp
dynamic db = dbConfig.CreateInstance();

IEnumerable<User> users = db.GetUsers<IEnumerable<User>>();
Console.WriteLine(users.First().Name);
```

Dynamic rows work too:

```csharp
dynamic db = dbConfig.CreateInstance();

IEnumerable<dynamic> users = db.GetUsers<IEnumerable<dynamic>>();
Console.WriteLine(users.First().name);
```

Dynamic async calls use the `Async` suffix and return awaitable tasks:

```csharp
dynamic db = dbConfig.CreateInstance();

int result = await db.AddAsync<int>(a: 6, b: 7);
await db.SaveUserAsync(username: "kamyar1979");
```

For dynamic stored procedure return values, add a trailing underscore before the
generic result type:

```csharp
int newId = await db.CreateUserAsync_<int>(username: "kamyar1979");
```

## Naming Runes

By default, Sporm uses your C# method, parameter, and property names. You can
shape those names before they cross the database gate:

```csharp
var dbConfig = ConfigurationBuilder
    .ForDatabase(connectionString, providerFactory)
    .Inflector(name => name.Underscore())
    .Deflector(name => name.Pascalize());
```

- `Inflector` converts C# names to database names, such as `GetUsers` to
  `get_users`.
- `Deflector` converts database result names to C# names for dynamic and
  dictionary results.
- `[DbName("database_name")]` overrides the name for one method, parameter, or
  property.

To make dynamic result properties use C#-style names, add a deflector:

```csharp
var dbConfig = ConfigurationBuilder
    .ForDatabase(connectionString, NpgsqlFactory.Instance)
    .Inflector(name => name.Underscore())
    .Deflector(name => name.Pascalize());
```

Then dynamic results can use `Name` instead of `name`.

## Parameters And Return Values

Sporm supports input parameters, output parameters, and stored procedure return
values.

```csharp
public interface IMyDb : IDisposable
{
    void UpdateName(long id, string name);

    void TryGetName(long id, [Size(256)] out string name);

    [ReturnValueAsResult]
    int CreateUser(string username, string email);

    [ReturnValueAsResult]
    Task<int> CreateUserAsync(string username, string email);
}
```

Useful attributes:

- `[DbName("...")]` sets the database name explicitly.
- `[Size(...)]` sets the size for an output parameter.
- `[ReturnValue]` marks a parameter as the stored procedure return-value
  parameter.
- `[ReturnValueAsResult]` maps the stored procedure return value to the method
  result.

If your provider or database needs custom CLR-to-`DbType` mapping, configure a
type resolver:

```csharp
var dbConfig = ConfigurationBuilder
    .ForDatabase(connectionString, providerFactory)
    .TypeResolver(member => DbType.String);
```

## Result Shapes

Sporm can extract:

- primitive values, such as `int`, `string`, and `DateTime`
- `Task<T>` primitive results
- `IEnumerable<T>` result sets
- `Task<IAsyncEnumerable<T>>` async result sets
- `Dictionary<string, object?>`
- dynamic row objects

## When To Use This Magic

Sporm is best when your database boundary is already built around stored
procedures or database functions and you want C# calls to stay small and
readable.

Good fits:

- legacy databases with important stored procedures
- data-driven projects where database functions are part of the design
- small data-access layers that should avoid repetitive ADO.NET plumbing
- services that want explicit database contracts without a full entity ORM

Reach for handwritten ADO.NET or a conventional ORM when:

- queries are complex and easier to review as SQL
- you need rich change tracking
- you need provider-specific features outside the stored-procedure model

## Development

Build the solution:

```bash
dotnet build
```

Run the test suite:

```bash
dotnet test
```

The PostgreSQL integration test is optional. To run it, provide a connection
string through `SPORM_POSTGRES_CONNECTION`:

```bash
SPORM_POSTGRES_CONNECTION="Host=localhost;Username=postgres;Password=secret;Database=example" dotnet test
```

The async behavior is covered by provider-level unit tests that do not require a
real database server.
