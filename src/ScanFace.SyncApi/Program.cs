using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using ScanFace.Domain;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

var databasePath = builder.Configuration["SCANFACE_DB_PATH"]
    ?? Path.Combine(AppContext.BaseDirectory, "data", "scanface-sync.db");
var configuredToken = builder.Configuration["SCANFACE_SYNC_TOKEN"]
    ?? (builder.Environment.IsDevelopment() ? "scanface-local-development" : string.Empty);

var database = new SyncDatabase(databasePath);
await database.InitializeAsync();

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/api"))
    {
        await next();
        return;
    }

    var suppliedToken = context.Request.Headers["X-ScanFace-Token"].ToString();
    if (string.IsNullOrWhiteSpace(configuredToken) || !SecureEquals(configuredToken, suppliedToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Token de sincronização inválido." });
        return;
    }

    await next();
});

app.MapHealthChecks("/health");

app.MapGet("/api/v1/vaults/{vaultId:guid}", async (Guid vaultId, CancellationToken cancellationToken) =>
{
    var document = await database.GetAsync(vaultId, cancellationToken);
    return document is null ? Results.NotFound() : Results.Ok(document);
});

app.MapPut("/api/v1/vaults/{vaultId:guid}", async (
    Guid vaultId,
    PutVaultRequest request,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Payload) || request.Payload.Length > 20_000_000)
    {
        return Results.BadRequest(new { error = "O payload deve ter entre 1 byte e 20 MB." });
    }

    long? expectedVersion = null;
    if (context.Request.Headers.TryGetValue("If-Match", out var rawVersion))
    {
        var normalized = rawVersion.ToString().Trim('"');
        if (!long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return Results.BadRequest(new { error = "Cabeçalho If-Match inválido." });
        }
        expectedVersion = parsed;
    }

    var result = await database.PutAsync(vaultId, request.Payload, expectedVersion, cancellationToken);
    return result is null
        ? Results.Conflict(new { error = "A versão remota mudou. Atualize antes de enviar novamente." })
        : Results.Ok(result);
});

app.Run();

static bool SecureEquals(string expected, string supplied)
{
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    try
    {
        return expectedBytes.Length == suppliedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
    finally
    {
        CryptographicOperations.ZeroMemory(expectedBytes);
        CryptographicOperations.ZeroMemory(suppliedBytes);
    }
}

public sealed record PutVaultRequest(string Payload);

public sealed class SyncDatabase
{
    private readonly string _connectionString;

    public SyncDatabase(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS vaults (
                vault_id TEXT PRIMARY KEY,
                payload TEXT NOT NULL,
                version INTEGER NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RemoteVaultDocument?> GetAsync(Guid vaultId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload, version, updated_at_utc FROM vaults WHERE vault_id = $id";
        command.Parameters.AddWithValue("$id", vaultId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RemoteVaultDocument(
            vaultId,
            reader.GetInt64(1),
            DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            reader.GetString(0));
    }

    public async Task<RemoteVaultDocument?> PutAsync(
        Guid vaultId,
        string payload,
        long? expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        long? currentVersion;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = (SqliteTransaction)transaction;
            read.CommandText = "SELECT version FROM vaults WHERE vault_id = $id";
            read.Parameters.AddWithValue("$id", vaultId.ToString("D"));
            var raw = await read.ExecuteScalarAsync(cancellationToken);
            currentVersion = raw is null ? null : Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        if ((currentVersion is null && expectedVersion is not null) ||
            (currentVersion is not null && expectedVersion != currentVersion))
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var nextVersion = (currentVersion ?? 0) + 1;
        var updatedAt = DateTimeOffset.UtcNow;
        await using (var write = connection.CreateCommand())
        {
            write.Transaction = (SqliteTransaction)transaction;
            write.CommandText = """
                INSERT INTO vaults(vault_id, payload, version, updated_at_utc)
                VALUES($id, $payload, $version, $updated)
                ON CONFLICT(vault_id) DO UPDATE SET
                    payload = excluded.payload,
                    version = excluded.version,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            write.Parameters.AddWithValue("$id", vaultId.ToString("D"));
            write.Parameters.AddWithValue("$payload", payload);
            write.Parameters.AddWithValue("$version", nextVersion);
            write.Parameters.AddWithValue("$updated", updatedAt.ToString("O"));
            await write.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RemoteVaultDocument(vaultId, nextVersion, updatedAt, payload);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}

public partial class Program;
