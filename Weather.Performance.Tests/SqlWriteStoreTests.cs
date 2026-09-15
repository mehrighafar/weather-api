using Xunit;

public sealed class SqlWriteStoreTests
{
    private readonly SqlWriteStore store;

    public SqlWriteStoreTests()
    {
        DotEnv.Load();
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQLSERVER_CONNECTION_STRING is required for SQL Server integration tests.");
        }

        store = new SqlWriteStore(connectionString);
        store.InitializeAsync().GetAwaiter().GetResult();
        store.ClearAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task AppendOnly_creates_a_new_row_for_each_write()
    {
        await store.AppendAsync(42, 100);
        await store.AppendAsync(42, 200);

        Assert.Equal(2, await store.CountAsync(SqlWriteStore.AppendTable, 42));
    }

    [Fact]
    public async Task Upsert_inserts_then_updates_the_same_logical_key()
    {
        await store.UpsertAsync(42, 100);
        await store.UpsertAsync(42, 200);

        Assert.Equal(1, await store.CountAsync(SqlWriteStore.UpsertTable, 42));
        Assert.Equal(200, await store.ValueAsync(SqlWriteStore.UpsertTable, 42));
    }

    [Fact]
    public async Task Concurrent_upserts_do_not_create_duplicate_keys()
    {
        var writes = Enumerable.Range(0, 32)
            .Select(value => store.UpsertAsync(7, value))
            .ToArray();

        await Task.WhenAll(writes);

        Assert.Equal(1, await store.CountAsync(SqlWriteStore.UpsertTable, 7));
    }
}
