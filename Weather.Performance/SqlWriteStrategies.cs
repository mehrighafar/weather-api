using System.Data;
using Microsoft.Data.SqlClient;

public sealed record SqlBenchmarkOptions(string ConnectionString)
{
    public static SqlBenchmarkOptions FromEnvironment()
    {
        DotEnv.Load();
        var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("SQLSERVER_CONNECTION_STRING is required in .env or the process environment.");
        }

        return new SqlBenchmarkOptions(connectionString);
    }
}

public static class DotEnv
{
    public static void Load()
    {
        var path = FindFile(".env");
        if (path is null)
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("export ", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
            }
        }
    }

    private static string? FindFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed class SqlWriteStore
{
    public const string AppendTable = "dbo.BenchmarkAppendRows";
    public const string UpsertTable = "dbo.BenchmarkUpsertRows";

    private readonly string connectionString;

    public SqlWriteStore(string connectionString) => this.connectionString = connectionString;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'{AppendTable}', N'U') IS NULL
                CREATE TABLE {AppendTable} (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkAppendRows PRIMARY KEY, WorkKey int NOT NULL, Value int NOT NULL, CreatedAt datetime2(3) NOT NULL);
            IF OBJECT_ID(N'{UpsertTable}', N'U') IS NULL
            BEGIN
                CREATE TABLE {UpsertTable} (Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkUpsertRows PRIMARY KEY, WorkKey int NOT NULL, Value int NOT NULL, CreatedAt datetime2(3) NOT NULL);
                CREATE UNIQUE INDEX UX_BenchmarkUpsertRows_WorkKey ON {UpsertTable}(WorkKey);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"TRUNCATE TABLE {AppendTable}; TRUNCATE TABLE {UpsertTable};";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAsync(int workKey, int value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {AppendTable}(WorkKey, Value, CreatedAt) VALUES (@workKey, @value, SYSUTCDATETIME());";
        command.Parameters.Add("@workKey", SqlDbType.Int).Value = workKey;
        command.Parameters.Add("@value", SqlDbType.Int).Value = value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertAsync(int workKey, int value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE {UpsertTable} WITH (UPDLOCK, SERIALIZABLE)
            SET Value = @value, CreatedAt = SYSUTCDATETIME()
            WHERE WorkKey = @workKey;
            IF @@ROWCOUNT = 0
                INSERT INTO {UpsertTable}(WorkKey, Value, CreatedAt) VALUES (@workKey, @value, SYSUTCDATETIME());
            """;
        command.Parameters.Add("@workKey", SqlDbType.Int).Value = workKey;
        command.Parameters.Add("@value", SqlDbType.Int).Value = value;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> CountAsync(string table, int workKey, CancellationToken cancellationToken = default)
    {
        if (table is not (AppendTable or UpsertTable)) throw new ArgumentException("Invalid benchmark table.", nameof(table));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE WorkKey = @workKey;";
        command.Parameters.Add("@workKey", SqlDbType.Int).Value = workKey;
        return (int)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<int> ValueAsync(string table, int workKey, CancellationToken cancellationToken = default)
    {
        if (table is not (AppendTable or UpsertTable)) throw new ArgumentException("Invalid benchmark table.", nameof(table));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Value FROM {table} WHERE WorkKey = @workKey;";
        command.Parameters.Add("@workKey", SqlDbType.Int).Value = workKey;
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Row not found."));
    }
}
