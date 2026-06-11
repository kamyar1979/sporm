using Sporm;

namespace TestSporm;

public interface IMyDb : IDisposable, IAsyncDisposable
{
    public int Add(int a, int b);
    public Task<int> AddAsync(int a, int b);
    public IEnumerable<User> GetUsers();
    public Task<IAsyncEnumerable<User>> GetUsersAsync();
}

public record struct User(
    long Id,
    [property: DbName("username")] string Username,
    string Name,
    string Email,
    DateTime LastLogin);