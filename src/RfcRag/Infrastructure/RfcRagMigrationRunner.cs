using System.Security.Cryptography;
using System.Text;

namespace RfcRag.Infrastructure;

internal static class RfcRagMigrationRunner
{
    public static string DefaultMigrationsDirectory =>
        Path.Join(AppContext.BaseDirectory, RfcRagConventions.MigrationsDirectoryName);

    public static Task ApplyAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken) =>
        ApplyAsync(dataSource, DefaultMigrationsDirectory, cancellationToken);

    public static async Task ApplyAsync(
        NpgsqlDataSource dataSource,
        string migrationsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationsDirectory);

        if (!Directory.Exists(migrationsDirectory))
        {
            throw new InvalidOperationException(
                $"RFC RAG PostgreSQL migrations directory '{migrationsDirectory}' does not exist.");
        }

        var migrations = Directory
            .EnumerateFiles(migrationsDirectory, RfcRagConventions.MigrationsSearchPattern)
            .Order(StringComparer.Ordinal)
            .Select(MigrationFile.Read)
            .ToArray();

        if (migrations.Length == 0)
        {
            throw new InvalidOperationException(
                $"No SQL migration files found in '{migrationsDirectory}'.");
        }

        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "select pg_advisory_lock(@LockKey)",
                new { LockKey = RfcRagConventions.MigrationLockKey },
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            try
            {
                foreach (var migration in migrations)
                {
                    await ApplyMigrationAsync(connection, migration, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    "select pg_advisory_unlock(@LockKey)",
                    new { LockKey = RfcRagConventions.MigrationLockKey },
                    cancellationToken: cancellationToken)).ConfigureAwait(false);
            }
        }
    }

    private static async Task ApplyMigrationAsync(
        NpgsqlConnection connection,
        MigrationFile migration,
        CancellationToken cancellationToken)
    {
        string? appliedChecksum = await TryGetAppliedMigrationChecksumAsync(
            connection,
            migration.FileName,
            cancellationToken).ConfigureAwait(false);

        if (appliedChecksum is not null)
        {
            if (!string.Equals(appliedChecksum, migration.ChecksumSha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"RFC RAG PostgreSQL migration '{migration.FileName}' checksum changed after it was applied.");
            }

            return;
        }

        var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (transaction.ConfigureAwait(false))
        {
            bool committed = false;
            try
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    migration.Sql,
                    transaction: transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await connection.ExecuteAsync(new CommandDefinition(
                    """
                    insert into rfc_rag.schema_migrations (filename, checksum_sha256)
                    values (@FileName, @ChecksumSha256)
                    """,
                    new
                    {
                        migration.FileName,
                        migration.ChecksumSha256
                    },
                    transaction,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                committed = true;
            }
            finally
            {
                if (!committed)
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string?> TryGetAppliedMigrationChecksumAsync(
        NpgsqlConnection connection,
        string fileName,
        CancellationToken cancellationToken)
    {
        bool migrationsTableExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'rfc_rag'
                  and table_name = 'schema_migrations'
            )
            """,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        if (!migrationsTableExists)
        {
            return null;
        }

        return await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            """
            select checksum_sha256
            from rfc_rag.schema_migrations
            where filename = @FileName
            """,
            new { FileName = fileName },
            cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public static async Task ValidateEmbeddingDimensionsAsync(
        NpgsqlDataSource dataSource,
        int expectedDimensions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                int? actualDimensions = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition(
                    """
                    select a.atttypmod
                    from pg_attribute a
                    join pg_class c on c.oid = a.attrelid
                    join pg_namespace n on n.oid = c.relnamespace
                    where n.nspname = 'rfc_rag'
                      and c.relname = 'rfc_sections'
                      and a.attname = 'embedding'
                      and a.attnum > 0
                    """,
                    cancellationToken: cancellationToken)).ConfigureAwait(false);

                if (actualDimensions is not null && actualDimensions.Value != expectedDimensions)
                {
                    throw new InvalidOperationException(
                        $"RFC RAG embedding dimension mismatch: the 'rfc_sections.embedding' column expects " +
                        $"{actualDimensions} dimensions, but RfcRagOptions.EmbeddingDimensions is configured to " +
                        $"{expectedDimensions}. Update EmbeddingDimensions in your configuration to match, or " +
                        $"change the embedding model to produce {actualDimensions}-dimensional vectors.");
                }
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (NpgsqlException ex)
        {
            logger.LogWarning(ex,
                "Could not validate RFC RAG embedding dimensions. " +
                "Skipping dimension check. Expected={ExpectedDimensions}",
                expectedDimensions);
        }
    }

    public static async Task ResetEmbeddingColumnAsync(
        NpgsqlDataSource dataSource,
        int dimensions,
        CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var _ = transaction.ConfigureAwait(false);

            // dimensions is a positive integer from application configuration — SQL injection is not possible.
            await connection.ExecuteAsync(new CommandDefinition( // NOSONAR
                $"""
                alter table rfc_rag.rfc_sections drop column if exists embedding;
                alter table rfc_rag.rfc_sections add column embedding vector({dimensions});
                """,
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed record class MigrationFile(string FileName, string Sql, string ChecksumSha256)
    {
        public static MigrationFile Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToUpperInvariant();
            string sql = Encoding.UTF8.GetString(bytes);
            return new MigrationFile(Path.GetFileName(path), sql, checksum);
        }
    }
}
