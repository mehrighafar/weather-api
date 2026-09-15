using System.Collections.Concurrent;
using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Data.SqlClient;

BenchmarkRunner.Run<SqlServerWriteBenchmark>();
return 0;

[MemoryDiagnoser]
[InProcess]
public class SqlServerWriteBenchmark
{
    private const string AppendTable = "dbo.BenchmarkAppendRows";
    private const string UpsertTable = "dbo.BenchmarkUpsertRows";
    private readonly string connectionString = SqlBenchmarkOptions.FromEnvironment().ConnectionString;

    [Params(2)]
    public int Workers { get; set; }

    [Params(100)]
    public int KeySpace { get; set; }

    [Params(20)]
    public int OperationsPerWorker { get; set; }

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'{AppendTable}', N'U') IS NULL
            BEGIN
                CREATE TABLE {AppendTable}
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkAppendRows PRIMARY KEY,
                    WorkKey int NOT NULL,
                    Value int NOT NULL,
                    CreatedAt datetime2(3) NOT NULL
                );
            END;
            IF OBJECT_ID(N'{UpsertTable}', N'U') IS NULL
            BEGIN
                CREATE TABLE {UpsertTable}
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkUpsertRows PRIMARY KEY,
                    WorkKey int NOT NULL,
                    Value int NOT NULL,
                    CreatedAt datetime2(3) NOT NULL
                );
                CREATE UNIQUE INDEX UX_BenchmarkUpsertRows_WorkKey
                    ON {UpsertTable}(WorkKey);
            END;
            """;
        await command.ExecuteNonQueryAsync();
        await SeedSnapshotAsync(connection);
    }

    [IterationSetup(Targets = new[] { nameof(AppendOnly), nameof(Upsert) })]
    public async Task ClearTables()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"TRUNCATE TABLE {AppendTable}; TRUNCATE TABLE {UpsertTable};";
        await command.ExecuteNonQueryAsync();
    }

    [Benchmark]
    public Task<long> AppendOnly() => RunConcurrent(AppendWorkerAsync, "append-only");

    [Benchmark]
    public Task<long> Upsert() => RunConcurrent(UpsertWorkerAsync, "upsert");

    [Benchmark]
    public Task<long> Update() => RunConcurrent(UpdateWorkerAsync, "update");

    [Benchmark]
    public Task<long> ReadLatestSnapshot() => RunConcurrent(ReadLatestSnapshotWorkerAsync, "read-latest-snapshot");

    [Benchmark]
    public Task<long> BatchCleanup() => RunConcurrent(BatchCleanupWorkerAsync, "batch-cleanup");

    private async Task<long> RunConcurrent(
        Func<int, ConcurrentBag<double>, CancellationToken, Task<long>> worker,
        string strategy)
    {
        using var cancellation = new CancellationTokenSource();
        var latencies = new ConcurrentBag<double>();
        var tasks = Enumerable.Range(0, Workers)
            .Select(workerId => worker(workerId, latencies, cancellation.Token))
            .ToArray();

        var started = Stopwatch.GetTimestamp();
        var results = await Task.WhenAll(tasks);
        var elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds;
        var operations = results.Sum();
        var ordered = latencies.OrderBy(value => value).ToArray();

        Console.WriteLine(
            $"{strategy,-11} workers={Workers,2} keys={KeySpace,7} " +
            $"ops={operations,8} RPS={operations / elapsedSeconds,10:N0} " +
            $"p50={Percentile(ordered, .50),8:N2}ms " +
            $"p95={Percentile(ordered, .95),8:N2}ms " +
            $"p99={Percentile(ordered, .99),8:N2}ms");

        return operations;
    }

    private async Task<long> AppendWorkerAsync(
        int workerId,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {AppendTable}(WorkKey, Value, CreatedAt)
            VALUES (@workKey, @value, SYSUTCDATETIME());
            """;
        var workKey = command.Parameters.Add("@workKey", System.Data.SqlDbType.Int);
        var value = command.Parameters.Add("@value", System.Data.SqlDbType.Int);

        return await ExecuteFixedCount(
            workerId,
            async sequence =>
            {
                workKey.Value = sequence % KeySpace;
                value.Value = sequence;
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            latencies,
            cancellationToken,
            OperationsPerWorker);
    }

    private async Task<long> UpdateWorkerAsync(
        int workerId,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            UPDATE {UpsertTable}
            SET Value = @value, CreatedAt = SYSUTCDATETIME()
            WHERE WorkKey = @workKey;
            """;
        command.Parameters.Add("@workKey", System.Data.SqlDbType.Int).Value = 910010;
        var value = command.Parameters.Add("@value", System.Data.SqlDbType.Int);

        return await ExecuteFixedCount(
            workerId,
            async sequence =>
            {
                value.Value = sequence;
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            latencies,
            cancellationToken,
            OperationsPerWorker);
    }

    private async Task<long> ReadLatestSnapshotWorkerAsync(
        int workerId,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ResponseJson FROM dbo.BenchmarkWeatherSnapshots WHERE QueryKey = @queryKey ORDER BY ReceivedAtUtc DESC, Id DESC;";
        command.Parameters.Add("@queryKey", System.Data.SqlDbType.Char, 64).Value = "diagnostic-key";

        return await ExecuteFixedCount(
            workerId,
            async _ =>
            {
                if (await command.ExecuteScalarAsync(cancellationToken) is not string payload || payload.Length == 0)
                    throw new InvalidOperationException("ReadLatestSnapshot returned no seeded snapshot.");
            },
            latencies,
            cancellationToken,
            OperationsPerWorker);
    }

    private async Task<long> BatchCleanupWorkerAsync(
        int workerId,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {AppendTable}(WorkKey, Value, CreatedAt)
            VALUES (@workKey, @value, SYSUTCDATETIME());
            DELETE TOP (100) FROM {AppendTable} WHERE WorkKey = @workKey;
            """;
        command.Parameters.Add("@workKey", System.Data.SqlDbType.Int).Value = 910020;
        var value = command.Parameters.Add("@value", System.Data.SqlDbType.Int);

        return await ExecuteFixedCount(
            workerId,
            async sequence =>
            {
                value.Value = sequence;
                await command.ExecuteNonQueryAsync(cancellationToken);
            },
            latencies,
            cancellationToken,
            OperationsPerWorker);
    }

    private static async Task SeedSnapshotAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.BenchmarkWeatherSnapshots', N'U') IS NULL
                CREATE TABLE dbo.BenchmarkWeatherSnapshots
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_BenchmarkWeatherSnapshots PRIMARY KEY,
                    QueryKey char(64) NOT NULL,
                    ResponseJson nvarchar(max) NOT NULL,
                    ReceivedAtUtc datetime2(7) NOT NULL
                );
            IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BenchmarkWeatherSnapshots_QueryKey_ReceivedAtUtc')
                CREATE INDEX IX_BenchmarkWeatherSnapshots_QueryKey_ReceivedAtUtc
                    ON dbo.BenchmarkWeatherSnapshots(QueryKey, ReceivedAtUtc DESC, Id DESC);
            IF NOT EXISTS (SELECT 1 FROM dbo.BenchmarkUpsertRows WHERE WorkKey = 910010)
                INSERT INTO dbo.BenchmarkUpsertRows(WorkKey, Value, CreatedAt)
                VALUES (910010, 0, SYSUTCDATETIME());
            UPDATE dbo.BenchmarkWeatherSnapshots
            SET ResponseJson = @payload, ReceivedAtUtc = SYSUTCDATETIME()
            WHERE QueryKey = @queryKey;
            IF @@ROWCOUNT = 0
                INSERT INTO dbo.BenchmarkWeatherSnapshots(QueryKey, ResponseJson, ReceivedAtUtc)
                VALUES (@queryKey, @payload, SYSUTCDATETIME());
            SELECT CAST(CASE WHEN EXISTS
                (SELECT 1 FROM dbo.BenchmarkWeatherSnapshots WHERE QueryKey = @queryKey)
                THEN 1 ELSE 0 END AS bit);
            """;
        command.Parameters.Add("@queryKey", System.Data.SqlDbType.Char, 64).Value = "diagnostic-key";
        command.Parameters.Add("@payload", System.Data.SqlDbType.NVarChar, -1).Value = "{\"diagnostic\":true}";
        var snapshotExists = (bool)(await command.ExecuteScalarAsync() ?? false);
        Console.WriteLine($"Snapshot exists: {snapshotExists}");
        if (!snapshotExists)
            throw new InvalidOperationException("GlobalSetup failed to seed the deterministic snapshot.");
    }

    private async Task<long> UpsertWorkerAsync(
        int workerId,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return await ExecuteFixedCount(
            workerId,
            async sequence =>
            {
                await using var operation = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.Transaction = operation;
                command.CommandText = $"""
                    UPDATE {UpsertTable} WITH (UPDLOCK, SERIALIZABLE)
                    SET Value = @value, CreatedAt = SYSUTCDATETIME()
                    WHERE WorkKey = @workKey;
                    IF @@ROWCOUNT = 0
                    BEGIN
                        INSERT INTO {UpsertTable}(WorkKey, Value, CreatedAt)
                        VALUES (@workKey, @value, SYSUTCDATETIME());
                    END;
                    """;
                command.Parameters.AddWithValue("@workKey", sequence % KeySpace);
                command.Parameters.AddWithValue("@value", sequence);
                await command.ExecuteNonQueryAsync(cancellationToken);
                await operation.CommitAsync(cancellationToken);
            },
            latencies,
            cancellationToken,
            OperationsPerWorker);
    }

    private static async Task<long> ExecuteFixedCount(
        int workerId,
        Func<int, Task> operation,
        ConcurrentBag<double> latencies,
        CancellationToken cancellationToken,
        int operationCount)
    {
        long completed = 0;
        var sequence = workerId;
        for (var operationIndex = 0; operationIndex < operationCount; operationIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            await operation(sequence);
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            completed++;
            sequence += Environment.ProcessorCount;
        }

        return completed;
    }

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return double.NaN;
        }

        var index = Math.Min(values.Length - 1, (int)Math.Ceiling(values.Length * percentile) - 1);
        return values[index];
    }
}
