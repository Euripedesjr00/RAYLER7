using System.Globalization;
using Rayler7.Core.Audit;

namespace Rayler7.Infrastructure.Persistence;

/// <summary>Histórico persistente: somente inclusão, nunca atualização ou exclusão.</summary>
public sealed class SqliteAuditLog(Database db) : IAuditLog
{
    public void Append(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_log(project_id, user_name, timestamp, operation, target, old_value, new_value, origin, reason)
            VALUES ($p, $u, $t, $o, $tg, $old, $new, $orig, $r)
            """;
        cmd.Parameters.AddWithValue("$p", entry.ProjectId.ToString());
        cmd.Parameters.AddWithValue("$u", entry.User);
        cmd.Parameters.AddWithValue("$t", entry.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$o", entry.Operation);
        cmd.Parameters.AddWithValue("$tg", entry.Target);
        cmd.Parameters.AddWithValue("$old", (object?)entry.OldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$new", (object?)entry.NewValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$orig", entry.Origin);
        cmd.Parameters.AddWithValue("$r", (object?)entry.Reason ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditEntry> Entries(Guid projectId)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT user_name, timestamp, operation, target, old_value, new_value, origin, reason FROM audit_log WHERE project_id = $p ORDER BY id";
        cmd.Parameters.AddWithValue("$p", projectId.ToString());
        using var r = cmd.ExecuteReader();
        var list = new List<AuditEntry>();
        while (r.Read())
        {
            list.Add(new AuditEntry(projectId, r.GetString(0),
                DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture),
                r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7)));
        }

        return list;
    }
}
