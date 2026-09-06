using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScanFace.Application;
using ScanFace.Domain;

namespace ScanFace.Infrastructure;

public sealed class SqliteVaultRepository : IVaultRepository
{
    private const string MetadataKey = "vault";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppPaths _paths;

    public SqliteVaultRepository(AppPaths paths) => _paths = paths;

    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(_paths.VaultDatabasePath));

    public async Task InitializeAsync(VaultMetadata metadata, CancellationToken cancellationToken = default)
    {
        _paths.EnsureCreated();
        if (File.Exists(_paths.VaultDatabasePath))
        {
            throw new InvalidOperationException("O banco do cofre já existe.");
        }

        await using var connection = await OpenAsync(cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);
        await WriteMetadataAsync(connection, metadata, null, cancellationToken);
    }

    public async Task<VaultMetadata?> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_paths.VaultDatabasePath))
        {
            return null;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = $key";
        command.Parameters.AddWithValue("$key", MetadataKey);
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return value is null ? null : JsonSerializer.Deserialize<VaultMetadata>(value, JsonOptions);
    }

    public async Task UpdateMetadataAsync(VaultMetadata metadata, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await WriteMetadataAsync(connection, metadata, null, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredEncryptedEntry>> GetEntriesAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<StoredEncryptedEntry>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, nonce, ciphertext, tag, updated_at_utc FROM entries ORDER BY updated_at_utc DESC";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new StoredEncryptedEntry(
                Guid.Parse(reader.GetString(0)),
                (byte[])reader[1],
                (byte[])reader[2],
                (byte[])reader[3],
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }
        return entries;
    }

    public async Task UpsertEntryAsync(StoredEncryptedEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO entries(id, nonce, ciphertext, tag, updated_at_utc)
            VALUES($id, $nonce, $ciphertext, $tag, $updated)
            ON CONFLICT(id) DO UPDATE SET
                nonce = excluded.nonce,
                ciphertext = excluded.ciphertext,
                tag = excluded.tag,
                updated_at_utc = excluded.updated_at_utc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue("$nonce", entry.Nonce);
        command.Parameters.AddWithValue("$ciphertext", entry.Ciphertext);
        command.Parameters.AddWithValue("$tag", entry.Tag);
        command.Parameters.AddWithValue("$updated", entry.UpdatedAtUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await TouchMetadataAsync(connection, (SqliteTransaction)transaction, entry.UpdatedAtUtc, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteEntryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM entries WHERE id = $id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        await TouchMetadataAsync(connection, (SqliteTransaction)transaction, DateTimeOffset.UtcNow, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PortableVaultSnapshot> ExportSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var metadata = await GetMetadataAsync(cancellationToken)
            ?? throw new InvalidOperationException("Nenhum cofre foi configurado.");
        return new PortableVaultSnapshot
        {
            Metadata = PortableMetadata(metadata),
            Entries = await GetEntriesAsync(cancellationToken)
        };
    }

    public async Task ReplaceFromSnapshotAsync(PortableVaultSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Metadata.SchemaVersion != VaultMetadata.CurrentSchemaVersion)
        {
            throw new InvalidDataException("A versão deste backup não é compatível com o aplicativo.");
        }

        _paths.EnsureCreated();
        await using var connection = await OpenAsync(cancellationToken);
        await CreateSchemaAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = "DELETE FROM entries";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var entry in snapshot.Entries)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT INTO entries(id, nonce, ciphertext, tag, updated_at_utc) VALUES($id, $nonce, $ciphertext, $tag, $updated)";
            insert.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
            insert.Parameters.AddWithValue("$nonce", entry.Nonce);
            insert.Parameters.AddWithValue("$ciphertext", entry.Ciphertext);
            insert.Parameters.AddWithValue("$tag", entry.Tag);
            insert.Parameters.AddWithValue("$updated", entry.UpdatedAtUtc.ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteMetadataAsync(connection, snapshot.Metadata, (SqliteTransaction)transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        _paths.EnsureCreated();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.VaultDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS entries (
                id TEXT PRIMARY KEY,
                nonce BLOB NOT NULL,
                ciphertext BLOB NOT NULL,
                tag BLOB NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteMetadataAsync(
        SqliteConnection connection,
        VaultMetadata metadata,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metadata(key, value) VALUES($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value";
        command.Parameters.AddWithValue("$key", MetadataKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(metadata, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task TouchMetadataAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT value FROM metadata WHERE key = $key";
        read.Parameters.AddWithValue("$key", MetadataKey);
        var json = await read.ExecuteScalarAsync(cancellationToken) as string
            ?? throw new InvalidDataException("Metadados do cofre ausentes.");
        var metadata = JsonSerializer.Deserialize<VaultMetadata>(json, JsonOptions)
            ?? throw new InvalidDataException("Metadados do cofre inválidos.");
        metadata.LastModifiedUtc = timestamp;
        await WriteMetadataAsync(connection, metadata, transaction, cancellationToken);
    }

    private static VaultMetadata PortableMetadata(VaultMetadata metadata) => new()
    {
        SchemaVersion = metadata.SchemaVersion,
        VaultId = metadata.VaultId,
        MasterSalt = metadata.MasterSalt.ToArray(),
        KeyDerivation = metadata.KeyDerivation,
        MasterKeyEnvelope = new EncryptedPayload(
            metadata.MasterKeyEnvelope.Nonce.ToArray(),
            metadata.MasterKeyEnvelope.Ciphertext.ToArray(),
            metadata.MasterKeyEnvelope.Tag.ToArray()),
        HelloKeyEnvelope = null,
        CreatedAtUtc = metadata.CreatedAtUtc,
        LastModifiedUtc = metadata.LastModifiedUtc
    };
}
