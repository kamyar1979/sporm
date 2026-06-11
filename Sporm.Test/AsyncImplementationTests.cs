using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

namespace Sporm.Test;

public class AsyncImplementationTests
{
    [Fact]
    public async Task TaskResult_uses_async_scalar_execution()
    {
        var scenario = new FakeDbScenario();
        await using var db = CreateDb(scenario);

        var result = await db.AddAsync(6, 7);

        Assert.Equal(13, result);
        Assert.Equal("add", scenario.Commands.Single().CommandText);
        Assert.Equal(1, scenario.OpenAsyncCount);
        Assert.Equal(0, scenario.OpenCount);
        Assert.Equal(1, scenario.ExecuteScalarAsyncCount);
        Assert.Equal(0, scenario.ExecuteScalarCount);
    }

    [Fact]
    public async Task Task_method_uses_async_non_query_execution()
    {
        var scenario = new FakeDbScenario();
        await using var db = CreateDb(scenario);

        await db.SaveUserAsync("kamyar");

        Assert.Equal("save_user", scenario.Commands.Single().CommandText);
        Assert.Equal(1, scenario.OpenAsyncCount);
        Assert.Equal(1, scenario.ExecuteNonQueryAsyncCount);
        Assert.Equal(0, scenario.ExecuteNonQueryCount);
    }

    [Fact]
    public async Task Async_enumerable_maps_rows_and_closes_reader()
    {
        var scenario = new FakeDbScenario
        {
            Rows =
            [
                new()
                {
                    ["id"] = 1L,
                    ["username"] = "kamyar1979",
                    ["name"] = "Kamyar Inanloo",
                    ["email"] = "kamyar1979@example.com",
                    ["last_login"] = new DateTime(2026, 6, 11)
                }
            ]
        };
        await using var db = CreateDb(scenario);

        var users = await db.GetUsersAsync();
        var result = new List<FakeUser>();

        await foreach (var item in users)
        {
            result.Add(item);
        }

        var mappedUser = Assert.Single(result);
        Assert.Equal(1L, mappedUser.Id);
        Assert.Equal("kamyar1979", mappedUser.Username);
        Assert.Equal("Kamyar Inanloo", mappedUser.Name);
        Assert.Equal(1, scenario.ExecuteReaderAsyncCount);
        Assert.Equal(1, scenario.ReaderCloseAsyncCount);
    }

    [Fact]
    public async Task Async_return_value_result_returns_the_typed_value()
    {
        var scenario = new FakeDbScenario { ReturnValue = 42 };
        await using var db = CreateDb(scenario);

        var result = await db.CreateUserAsync("kamyar1979");

        Assert.Equal(42, result);
        Assert.Equal("create_user", scenario.Commands.Single().CommandText);
        Assert.Equal(1, scenario.ExecuteNonQueryAsyncCount);
    }

    [Fact]
    public async Task Dynamic_async_result_returns_the_typed_value()
    {
        var scenario = new FakeDbScenario();
        dynamic db = ConfigurationBuilder
            .ForDatabase("fake", new FakeDbProviderFactory(scenario))
            .Inflector(ToDatabaseName)
            .CreateInstance();

        int result = await db.AddAsync<int>(a: 2, b: 3);

        Assert.Equal(5, result);
        Assert.Equal("add", scenario.Commands.Single().CommandText);
        Assert.Equal(1, scenario.OpenAsyncCount);
        Assert.Equal(0, scenario.OpenCount);
        Assert.Equal(1, scenario.ExecuteScalarAsyncCount);
    }

    [Fact]
    public async Task Dynamic_async_non_query_returns_task_and_uses_async_execution()
    {
        var scenario = new FakeDbScenario();
        dynamic db = ConfigurationBuilder
            .ForDatabase("fake", new FakeDbProviderFactory(scenario))
            .Inflector(ToDatabaseName)
            .CreateInstance();

        await db.SaveUserAsync(username: "kamyar");

        Assert.Equal("save_user", scenario.Commands.Single().CommandText);
        Assert.Equal(1, scenario.OpenAsyncCount);
        Assert.Equal(0, scenario.OpenCount);
        Assert.Equal(1, scenario.ExecuteNonQueryAsyncCount);
        Assert.Equal(0, scenario.ExecuteNonQueryCount);
    }

    [Fact]
    public async Task Dynamic_async_return_value_returns_typed_task()
    {
        var scenario = new FakeDbScenario { ReturnValue = 42 };
        dynamic db = ConfigurationBuilder
            .ForDatabase("fake", new FakeDbProviderFactory(scenario))
            .Inflector(ToDatabaseName)
            .CreateInstance();

        int result = await db.CreateUserAsync_<int>(username: "kamyar");

        Assert.Equal(42, result);
        Assert.Equal("create_user", scenario.Commands.Single().CommandText);
        Assert.Equal(1, scenario.OpenAsyncCount);
        Assert.Equal(0, scenario.OpenCount);
        Assert.Equal(1, scenario.ExecuteNonQueryAsyncCount);
        Assert.Equal(0, scenario.ExecuteNonQueryCount);
    }

    private static IAsyncTestDb CreateDb(FakeDbScenario scenario)
    {
        return ConfigurationBuilder
            .ForDatabase("fake", new FakeDbProviderFactory(scenario))
            .Inflector(ToDatabaseName)
            .CreateInstance<IAsyncTestDb>();
    }

    private static string ToDatabaseName(string name)
    {
        return string.Concat(name.SelectMany((c, i) =>
            i > 0 && char.IsUpper(c)
                ? ['_', char.ToLowerInvariant(c)]
                : new[] { char.ToLowerInvariant(c) }));
    }

    public interface IAsyncTestDb : IAsyncDisposable
    {
        Task<int> AddAsync(int a, int b);
        Task SaveUserAsync(string username);
        Task<IAsyncEnumerable<FakeUser>> GetUsersAsync();

        [ReturnValueAsResult]
        Task<int> CreateUserAsync(string username);
    }

    public class FakeUser
    {
        public long Id { get; set; }

        [DbName("username")]
        public string Username { get; set; } = "";

        public string Name { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime LastLogin { get; set; }
    }

    private sealed class FakeDbScenario
    {
        public List<FakeDbCommand> Commands { get; } = [];
        public List<Dictionary<string, object?>> Rows { get; init; } = [];
        public object? ReturnValue { get; init; }
        public int OpenCount { get; set; }
        public int OpenAsyncCount { get; set; }
        public int ExecuteScalarCount { get; set; }
        public int ExecuteScalarAsyncCount { get; set; }
        public int ExecuteNonQueryCount { get; set; }
        public int ExecuteNonQueryAsyncCount { get; set; }
        public int ExecuteReaderAsyncCount { get; set; }
        public int ReaderCloseAsyncCount { get; set; }
    }

    private sealed class FakeDbProviderFactory(FakeDbScenario scenario) : DbProviderFactory
    {
        public override DbConnection CreateConnection() => new FakeDbConnection(scenario);
        public override DbCommand CreateCommand() => new FakeDbCommand(scenario);
        public override DbParameter CreateParameter() => new FakeDbParameter();
    }

    private sealed class FakeDbConnection(FakeDbScenario scenario) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "fake";
        public override string DataSource => "fake";
        public override string ServerVersion => "1";
        public override ConnectionState State => _state;

        public override void Open()
        {
            scenario.OpenCount++;
            _state = ConnectionState.Open;
        }

        public override Task OpenAsync(CancellationToken cancellationToken)
        {
            scenario.OpenAsyncCount++;
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        public override void Close() => _state = ConnectionState.Closed;
        public override void ChangeDatabase(string databaseName) { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        protected override DbCommand CreateDbCommand() => new FakeDbCommand(scenario) { Connection = this };
    }

    private sealed class FakeDbCommand : DbCommand
    {
        private readonly FakeDbScenario _scenario;
        private readonly FakeDbParameterCollection _parameters = new();

        public FakeDbCommand(FakeDbScenario scenario)
        {
            _scenario = scenario;
            scenario.Commands.Add(this);
        }

        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }

        public override void Cancel() { }
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new FakeDbParameter();

        public override object? ExecuteScalar()
        {
            _scenario.ExecuteScalarCount++;
            return ExecuteScalarCore();
        }

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            _scenario.ExecuteScalarAsyncCount++;
            return Task.FromResult(ExecuteScalarCore());
        }

        public override int ExecuteNonQuery()
        {
            _scenario.ExecuteNonQueryCount++;
            SetReturnValue();
            return 1;
        }

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            _scenario.ExecuteNonQueryAsyncCount++;
            SetReturnValue();
            return Task.FromResult(1);
        }

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            return new FakeDbDataReader(_scenario);
        }

        protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            _scenario.ExecuteReaderAsyncCount++;
            return Task.FromResult<DbDataReader>(new FakeDbDataReader(_scenario));
        }

        private object? ExecuteScalarCore()
        {
            if (CommandText != "add") return null;

            return _parameters
                .Cast<FakeDbParameter>()
                .Where(param => param.Direction == ParameterDirection.Input)
                .Sum(param => Convert.ToInt32(param.Value));
        }

        private void SetReturnValue()
        {
            foreach (var parameter in _parameters.Cast<FakeDbParameter>())
            {
                if (parameter.Direction == ParameterDirection.ReturnValue)
                    parameter.Value = _scenario.ReturnValue;
            }
        }

        public override string ToString() => CommandText;
    }

    private sealed class FakeDbParameter : DbParameter
    {
        public override DbType DbType { get; set; }
        public override ParameterDirection Direction { get; set; }
        public override bool IsNullable { get; set; }
        [AllowNull]
        public override string ParameterName { get; set; } = "";

        [AllowNull]
        public override string SourceColumn { get; set; } = "";
        public override object? Value { get; set; }
        public override bool SourceColumnNullMapping { get; set; }
        public override int Size { get; set; }
        public override void ResetDbType() { }
    }

    private sealed class FakeDbParameterCollection : DbParameterCollection
    {
        private readonly List<DbParameter> _parameters = [];

        public override int Count => _parameters.Count;
        public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

        public override int Add(object value)
        {
            _parameters.Add((DbParameter)value);
            return _parameters.Count - 1;
        }

        public override void AddRange(Array values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }

        public override void Clear() => _parameters.Clear();
        public override bool Contains(object value) => _parameters.Contains((DbParameter)value);
        public override bool Contains(string value) => _parameters.Any(p => p.ParameterName == value);
        public override void CopyTo(Array array, int index) => _parameters.ToArray().CopyTo(array, index);
        public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();
        public override int IndexOf(object value) => _parameters.IndexOf((DbParameter)value);
        public override int IndexOf(string parameterName) => _parameters.FindIndex(p => p.ParameterName == parameterName);
        public override void Insert(int index, object value) => _parameters.Insert(index, (DbParameter)value);
        public override void Remove(object value) => _parameters.Remove((DbParameter)value);
        public override void RemoveAt(int index) => _parameters.RemoveAt(index);
        public override void RemoveAt(string parameterName) => _parameters.RemoveAt(IndexOf(parameterName));
        protected override DbParameter GetParameter(int index) => _parameters[index];
        protected override DbParameter GetParameter(string parameterName) => _parameters[IndexOf(parameterName)];
        protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;
        protected override void SetParameter(string parameterName, DbParameter value) => _parameters[IndexOf(parameterName)] = value;
    }

    private sealed class FakeDbDataReader(FakeDbScenario scenario) : DbDataReader
    {
        private int _index = -1;

        public override int FieldCount => scenario.Rows.FirstOrDefault()?.Count ?? 0;
        public override bool HasRows => scenario.Rows.Count > 0;
        private bool _isClosed;

        public override bool IsClosed => _isClosed;
        public override int RecordsAffected => 0;
        public override int Depth => 0;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => CurrentRow[name] ?? DBNull.Value;

        private Dictionary<string, object?> CurrentRow => scenario.Rows[_index];

        public override bool Read()
        {
            _index++;
            return _index < scenario.Rows.Count;
        }

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Read());
        }

        public override string GetName(int ordinal) => scenario.Rows[0].Keys.ElementAt(ordinal);
        public override int GetOrdinal(string name) => scenario.Rows[0].Keys.ToList().IndexOf(name);
        public override object GetValue(int ordinal) => CurrentRow[GetName(ordinal)] ?? DBNull.Value;
        public override Type GetFieldType(int ordinal) => GetValue(ordinal).GetType();
        public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;
        public override int GetValues(object[] values)
        {
            var count = Math.Min(values.Length, FieldCount);
            for (var i = 0; i < count; i++)
            {
                values[i] = GetValue(i);
            }

            return count;
        }

        public override void Close() => _isClosed = true;

        public override Task CloseAsync()
        {
            scenario.ReaderCloseAsyncCount++;
            _isClosed = true;
            return Task.CompletedTask;
        }

        public override bool NextResult() => false;
        public override IEnumerator GetEnumerator() => scenario.Rows.GetEnumerator();
        public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);
        public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public override char GetChar(int ordinal) => (char)GetValue(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;
        public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);
        public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);
        public override double GetDouble(int ordinal) => (double)GetValue(ordinal);
        public override float GetFloat(int ordinal) => (float)GetValue(ordinal);
        public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);
        public override short GetInt16(int ordinal) => (short)GetValue(ordinal);
        public override int GetInt32(int ordinal) => (int)GetValue(ordinal);
        public override long GetInt64(int ordinal) => (long)GetValue(ordinal);
        public override string GetString(int ordinal) => (string)GetValue(ordinal);
    }
}
