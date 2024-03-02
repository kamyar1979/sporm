# Stored Procedure ORM!

I know Stored Procedures and Database Functions, in most cases are not good practice, except for Data Driven projects. 
But this project has started years ago for communication with an old MS-SQL Server database and that is why I have used
the name 'Stored Procedure' instead of Database Function. But current version supports any database which has 
.NET driver.

## The problem
Many times, you want to call database functions/procedures from within an OOP project, and you have to write too much 
boiler plate code: You have to wrote code for adding parameters and values. If the functions returns a table structure, 
the ORM can not help you for impedance mismatch here! You have to manually convert between database result and your 
model objects. This library does the trick for you and tries to do all the things ORM does. You can call database 
functions as normal class methods and expect the result converted to .NET types, including your class types.

## Getting started

You can use this library in two ways: 

### 1. Static Typing

For static typing, you must create an interface which includes your database functions signature.

```sql
CREATE FUNCTION add(a integer, b integer) RETURNS integer
LANGUAGE SQL
IMMUTABLE
RETURNS NULL ON NULL INPUT
RETURN a + b;
```

```csharp
public interface IMyDb : IDisposable
{
    public int Add(int a, int b);
}
```

#### Note that IDisposable is _required_ to make the library close connection after reading data.

For PostgreSQL, we need to add the following line to force NpgSQL to behave functions as like stored procedures

```csharp
AppContext.SetSwitch("Npgsql.EnableStoredProcedureCompatMode", true);
```

Now, create configuration for your desired database, which includes functions:

```csharp

using Sporm;
using Inflector;

var conf = ConfigurationBuilder.ForDatabase("server=127.0.0.1;user id=kamyar;password=MySecretPassword;database=example",
    Npgsql.NpgsqlFactory.Instance).Inflector(s => s.Underscore()).Build();
```
(We are using Inflector library https://www.nuget.org/packages/Inflector.NetCore)

You can use any database factory you want instead of NpgsqlFactory! We are using PostGreSQL here. Then setup
your database! And you can now use Sprom!

```csharp

Setup.Register<IMyDb>(conf);

using var db = Setup.GetInstance<IMyDb>();

int result = db.Add(6, 7);

// Result would be 13!
```
Note the _using_ statement which makes the database connection close after reading data. You must use it to avoid connection
leaks in your project. You may note that we are using inflector, which means the naming convention of the database 
differs from code naming conventions. That is, if we used 'Add' in the function definition within the database, 
we would omit inflector part of the configuration.

Lets try a more complex example: The function returns a table.

```sql
CREATE TABLE IF NOT EXISTS public.users
(
    id bigint NOT NULL,
    username character varying(60) NOT NULL,
    password character varying(128),
    name character varying(256),
    email character varying(256),
    last_login timestamp with time zone,
    CONSTRAINT users_pk PRIMARY KEY (id),
    CONSTRAINT users_ak UNIQUE (username)
);

INSERT INTO users VALUES(1, 'kamyar1979', '<some hash>', 'Kamyar Inanloo', 'kamyar1979@gmail.com', CURRENT_DATE);
INSERT INTO users VALUES(2, 'otheruser', '<some hash>', 'Other User', 'someone@example.com', CURRENT_DATE);

CREATE OR REPLACE FUNCTION get_users() RETURNS TABLE (
	id bigint,
    username character varying(60),
    name character varying(256),
    email character varying(256),
    last_login timestamp with time zone)
	LANGUAGE PLPGSQL AS
	$$
BEGIN
RETURN QUERY SELECT users.id,users.username,users.name,users.email,users.last_login FROM users;
END
	$$
```

```csharp
public record struct User(
    long Id,
    [property: DbName("username")] string Username,
    string Name,
    string Email,
    DateTime LastLogin);```
```

```csharp

var result = db.GetUsers().ToArray();

Console.WriteLine(result[0].Name);

// Kamyar Inanloo

```

The result is an array of User object containing the result set. The shining part of this code is DbName attribute: The
inflector tries to find user_name in the result fields, but the name is username, so we explicitly inform the library to
use the mentioned name for the result. This attribute is available also for input parameters and class/struct names.

### 2. Dynamic Typing

What if we do not have time for creating database interface? No Problem! you just loose IDE 
auto-completion and some running speed (Due to DLR). In the following code we have not declared IMyDb interface,
instead we use dynamic keyword for database object;

```csharp
dynamic db = Setup.GetInstance(conf);

IEnumerable<User> result = db.GetUsers<IEnumerable<User>>();

var array = result.ToArray(); 

Console.WriteLine(array[0].Name);
```

What if we do not want to declare User class? It is possible too!

```csharp
dynamic db = Setup.GetInstance(conf);

IEnumerable<dynamic> result = db.GetUsers<IEnumerable<dynamic>>();

var array = result.ToArray(); 

Console.WriteLine(array[0].name);
```

Note that reverse inflection has not been supported yet. You must use the same property name as the function result.




