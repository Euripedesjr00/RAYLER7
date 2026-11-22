using System.Globalization;
using Rayler7.Infrastructure.Persistence;
using Rayler7.Infrastructure.Spreadsheets;

namespace Rayler7.Infrastructure.Projects;

public sealed record ProjectInfo(
    Guid Id,
    string Name,
    string? Description,
    string Owner,
    string Folder,
    string? Instructions,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt);

public sealed record ProjectFile(Guid Id, Guid ProjectId, string Role, string OriginalName, string StoredPath, string Sha256, DateTimeOffset AddedAt,
    string Status = "fila", string? StatusMessage = null);

/// <summary>
/// Projetos ficam no banco local + uma pasta própria em %LOCALAPPDATA%\RAYLER7\projetos\{id}.
/// Arquivos do usuário são COPIADOS para "originais" (com hash) e nunca editados.
/// </summary>
public sealed class ProjectService(Database db, string projectsRoot)
{
    public const string RoleTemplate = "modelo";
    public const string RoleSource = "origem";

    public ProjectInfo Create(string name, string owner, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var id = Guid.NewGuid();
        var folder = Path.Combine(projectsRoot, id.ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "originais"));
        Directory.CreateDirectory(Path.Combine(folder, "trabalho"));
        Directory.CreateDirectory(Path.Combine(folder, "exportacoes"));
        var now = DateTimeOffset.UtcNow;
        Exec("""
            INSERT INTO projects(id, name, description, owner, folder, instructions, archived, created_at, modified_at)
            VALUES ($id, $n, $d, $o, $f, NULL, 0, $t, $t)
            """, ("$id", id.ToString()), ("$n", name.Trim()), ("$d", description), ("$o", owner), ("$f", folder), ("$t", Iso(now)));
        return new ProjectInfo(id, name.Trim(), description, owner, folder, null, false, now, now);
    }

    public IReadOnlyList<ProjectInfo> Recent(int max = 20)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, owner, folder, instructions, archived, created_at, modified_at FROM projects WHERE archived = 0 ORDER BY modified_at DESC LIMIT $m";
        cmd.Parameters.AddWithValue("$m", max);
        using var r = cmd.ExecuteReader();
        var list = new List<ProjectInfo>();
        while (r.Read())
        {
            list.Add(Read(r));
        }

        return list;
    }

    public ProjectInfo? Get(Guid id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, name, description, owner, folder, instructions, archived, created_at, modified_at FROM projects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id.ToString());
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public void SaveInstructions(Guid projectId, string? instructions)
    {
        Exec("UPDATE projects SET instructions = $i, modified_at = $t WHERE id = $id",
            ("$i", instructions), ("$t", Iso(DateTimeOffset.UtcNow)), ("$id", projectId.ToString()));
    }

    public void Rename(Guid projectId, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Exec("UPDATE projects SET name = $n, modified_at = $t WHERE id = $id",
            ("$n", newName.Trim()), ("$t", Iso(DateTimeOffset.UtcNow)), ("$id", projectId.ToString()));
    }

    public void Touch(Guid projectId) =>
        Exec("UPDATE projects SET modified_at = $t WHERE id = $id", ("$t", Iso(DateTimeOffset.UtcNow)), ("$id", projectId.ToString()));

    /// <summary>Copia o arquivo do usuário para a pasta do projeto. O original do usuário não é tocado.</summary>
    public ProjectFile ImportFile(ProjectInfo project, string sourcePath, string role)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var id = Guid.NewGuid();
        var name = Path.GetFileName(sourcePath);
        var dest = Path.Combine(project.Folder, "originais", $"{id:N}_{name}");
        File.Copy(sourcePath, dest, overwrite: false);
        File.SetAttributes(dest, File.GetAttributes(dest) | FileAttributes.ReadOnly);
        var hash = FileHash.Sha256(dest);
        if (hash != FileHash.Sha256(sourcePath))
        {
            throw new IOException("A cópia do arquivo não confere com o original (hash diferente).");
        }

        var now = DateTimeOffset.UtcNow;
        Exec("""
            INSERT INTO project_files(id, project_id, role, original_name, stored_path, sha256, added_at)
            VALUES ($id, $p, $r, $n, $s, $h, $t)
            """, ("$id", id.ToString()), ("$p", project.Id.ToString()), ("$r", role), ("$n", name), ("$s", dest), ("$h", hash), ("$t", Iso(now)));
        Touch(project.Id);
        return new ProjectFile(id, project.Id, role, name, dest, hash, now);
    }

    public IReadOnlyList<ProjectFile> Files(Guid projectId, string? role = null)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, role, original_name, stored_path, sha256, added_at, status, status_message FROM project_files WHERE project_id = $p"
                          + (role is null ? string.Empty : " AND role = $r") + " ORDER BY added_at";
        cmd.Parameters.AddWithValue("$p", projectId.ToString());
        if (role is not null)
        {
            cmd.Parameters.AddWithValue("$r", role);
        }

        using var r = cmd.ExecuteReader();
        var list = new List<ProjectFile>();
        while (r.Read())
        {
            list.Add(new ProjectFile(Guid.Parse(r.GetString(0)), projectId, r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                DateTimeOffset.Parse(r.GetString(5), CultureInfo.InvariantCulture), r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
        }

        return list;
    }

    public void SetFileStatus(Guid fileId, string status, string? message) =>
        Exec("UPDATE project_files SET status = $s, status_message = $m WHERE id = $id",
            ("$s", status), ("$m", message), ("$id", fileId.ToString()));

    /// <summary>Retira o arquivo da fila do projeto (a cópia guardada é mantida para auditoria).</summary>
    public void RemoveFromQueue(Guid fileId) =>
        Exec("UPDATE project_files SET role = 'removido' WHERE id = $id", ("$id", fileId.ToString()));

    public void SetConference(Guid projectId, bool confirmed, string? filledPath) =>
        Exec("UPDATE projects SET conference_confirmed = $c, filled_path = $f, modified_at = $t WHERE id = $id",
            ("$c", confirmed ? 1 : 0), ("$f", filledPath), ("$t", Iso(DateTimeOffset.UtcNow)), ("$id", projectId.ToString()));

    public (bool Confirmed, string? FilledPath) GetConference(Guid projectId)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT conference_confirmed, filled_path FROM projects WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", projectId.ToString());
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt64(0) == 1, r.IsDBNull(1) ? null : r.GetString(1)) : (false, null);
    }

    /// <summary>Confere se os arquivos guardados continuam íntegros (hash).</summary>
    public IReadOnlyList<ProjectFile> VerifyIntegrity(Guid projectId) =>
        Files(projectId).Where(f => !File.Exists(f.StoredPath) || FileHash.Sha256(f.StoredPath) != f.Sha256).ToList();

    private static ProjectInfo Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5), r.GetInt64(6) == 1,
        DateTimeOffset.Parse(r.GetString(7), CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(r.GetString(8), CultureInfo.InvariantCulture));

    private static string Iso(DateTimeOffset t) => t.ToString("O", CultureInfo.InvariantCulture);

    private void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args)
        {
            cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }
}
