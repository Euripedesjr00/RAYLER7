using Microsoft.Data.Sqlite;

namespace Rayler7.Infrastructure.Persistence;

/// <summary>
/// Banco SQLite local com migrações versionadas em SQL explícito.
/// Escolha justificada: SQL legível e auditável, sem ferramentas extras de geração.
/// </summary>
public sealed class Database
{
    private readonly string _connectionString;

    public Database(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
        }.ToString();
    }

    public string FilePath { get; }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
        pragma.ExecuteNonQuery();
        return c;
    }

    public int CurrentVersion()
    {
        using var c = Open();
        EnsureVersionTable(c);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version";
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Aplica as migrações pendentes, cada uma em sua própria transação.</summary>
    public void Migrate()
    {
        using var c = Open();
        EnsureVersionTable(c);
        var current = CurrentVersion();
        foreach (var m in Migrations.All.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var tx = c.BeginTransaction();
            using (var cmd = c.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = m.Sql;
                cmd.ExecuteNonQuery();
            }

            using (var mark = c.CreateCommand())
            {
                mark.Transaction = tx;
                mark.CommandText = "INSERT INTO schema_version(version, name, applied_at) VALUES ($v, $n, $t)";
                mark.Parameters.AddWithValue("$v", m.Version);
                mark.Parameters.AddWithValue("$n", m.Name);
                mark.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                mark.ExecuteNonQuery();
            }

            tx.Commit();
        }
    }

    private static void EnsureVersionTable(SqliteConnection c)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS schema_version(version INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
    }
}

public sealed record Migration(int Version, string Name, string Sql);

public static class Migrations
{
    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, "usuarios_projetos_auditoria", """
            CREATE TABLE users(
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                display_name TEXT NOT NULL,
                role TEXT NOT NULL CHECK(role IN ('Administrador','Usuario')),
                password_hash TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                failed_attempts INTEGER NOT NULL DEFAULT 0,
                lockout_until TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE projects(
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                description TEXT NULL,
                owner TEXT NOT NULL,
                folder TEXT NOT NULL,
                instructions TEXT NULL,
                archived INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                modified_at TEXT NOT NULL
            );
            CREATE TABLE project_files(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id),
                role TEXT NOT NULL,
                original_name TEXT NOT NULL,
                stored_path TEXT NOT NULL,
                sha256 TEXT NOT NULL,
                added_at TEXT NOT NULL
            );
            CREATE TABLE audit_log(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                project_id TEXT NOT NULL,
                user_name TEXT NOT NULL,
                timestamp TEXT NOT NULL,
                operation TEXT NOT NULL,
                target TEXT NOT NULL,
                old_value TEXT NULL,
                new_value TEXT NULL,
                origin TEXT NOT NULL,
                reason TEXT NULL
            );
            CREATE INDEX ix_audit_project ON audit_log(project_id);
            CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """),
        new(2, "mapeamentos", """
            CREATE TABLE column_mappings(
                project_id TEXT NOT NULL REFERENCES projects(id),
                column_index INTEGER NOT NULL,
                header TEXT NULL,
                meaning TEXT NOT NULL,
                expect_numeric INTEGER NOT NULL DEFAULT 0,
                dictionary_json TEXT NOT NULL DEFAULT '{}',
                source TEXT NOT NULL,
                PRIMARY KEY(project_id, column_index)
            );
            ALTER TABLE projects ADD COLUMN mapping_confirmed INTEGER NOT NULL DEFAULT 0;
            """),
        new(3, "extracao", """
            ALTER TABLE project_files ADD COLUMN status TEXT NOT NULL DEFAULT 'fila';
            ALTER TABLE project_files ADD COLUMN status_message TEXT NULL;
            CREATE TABLE extracted_records(
                id TEXT PRIMARY KEY,
                project_id TEXT NOT NULL REFERENCES projects(id),
                file_id TEXT NULL,
                source_file TEXT NOT NULL,
                page INTEGER NULL,
                row_number INTEGER NULL,
                source_col TEXT NULL,
                target_col TEXT NULL,
                field TEXT NOT NULL,
                original TEXT NOT NULL,
                confidence REAL NULL,
                doubts_json TEXT NOT NULL,
                status TEXT NOT NULL,
                corrected TEXT NULL,
                leave_empty INTEGER NOT NULL DEFAULT 0,
                selected INTEGER NOT NULL DEFAULT 0,
                seq INTEGER NOT NULL
            );
            CREATE INDEX ix_records_project ON extracted_records(project_id, seq);
            ALTER TABLE projects ADD COLUMN conference_confirmed INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE projects ADD COLUMN filled_path TEXT NULL;
            """),
        new(4, "posicao_ocr", """
            ALTER TABLE extracted_records ADD COLUMN bbox_json TEXT NULL;
            """),
    ];
}
