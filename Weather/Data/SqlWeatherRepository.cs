using System.Data;
using Microsoft.Data.SqlClient;

namespace Weather.Data;

public sealed class SqlWeatherRepository : IWeatherRepository
{
    private const string TableName = "dbo.WeatherSnapshots";
    private readonly string connectionString;

    public SqlWeatherRepository(IConfiguration configuration)
    {
        connectionString = configuration["SQLSERVER_CONNECTION_STRING"]
            ?? Environment.GetEnvironmentVariable("SQLSERVER_CONNECTION_STRING")
            ?? throw new InvalidOperationException("SQLSERVER_CONNECTION_STRING is required.");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'{TableName}', N'U') IS NULL
            BEGIN
                CREATE TABLE {TableName}
                (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_WeatherSnapshots PRIMARY KEY,
                    QueryKey char(64) NOT NULL,
                    ResponseJson nvarchar(max) NOT NULL,
                    ReceivedAtUtc datetime2(7) NOT NULL
                );
                CREATE INDEX IX_WeatherSnapshots_QueryKey_ReceivedAtUtc
                    ON {TableName}(QueryKey, ReceivedAtUtc DESC, Id DESC);
                CREATE INDEX IX_WeatherSnapshots_ReceivedAtUtc
                    ON {TableName}(ReceivedAtUtc);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAsync(string queryKey, string payload, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO {TableName}(QueryKey, ResponseJson, ReceivedAtUtc)
            VALUES (@queryKey, @payload, SYSUTCDATETIME());
            """;
        command.Parameters.Add("@queryKey", SqlDbType.Char, 64).Value = queryKey;
        command.Parameters.Add("@payload", SqlDbType.NVarChar, -1).Value = payload;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetLatestAsync(string queryKey, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TOP (1) ResponseJson FROM {TableName} WHERE QueryKey = @queryKey ORDER BY ReceivedAtUtc DESC, Id DESC;";
        command.Parameters.Add("@queryKey", SqlDbType.Char, 64).Value = queryKey;
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task<int> CleanupExpiredAsync(DateTime receivedBeforeUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE TOP (1000) FROM {TableName} WHERE ReceivedAtUtc < @receivedBeforeUtc;";
        command.Parameters.Add("@receivedBeforeUtc", SqlDbType.DateTime2).Value = receivedBeforeUtc;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
