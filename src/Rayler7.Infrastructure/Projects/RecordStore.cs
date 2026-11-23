using System.Globalization;
using System.Text.Json;
using Rayler7.Core.Extraction;
using Rayler7.Core.Values;
using Rayler7.Infrastructure.Persistence;

namespace Rayler7.Infrastructure.Projects;

public sealed record StoredRecord(ExtractedRecord Record, Guid? FileId, bool Selected);

/// <summary>Guarda registros extraídos, seu estado de revisão e a seleção, para continuar depois.</summary>
public sealed class RecordStore(Database db)
{
    public void Append(Guid projectId, Guid? fileId, IEnumerable<ExtractedRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        long seq;
        using (var q = c.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT COALESCE(MAX(seq), 0) FROM extracted_records WHERE project_id = $p";
            q.Parameters.AddWithValue("$p", projectId.ToString());
            seq = Convert.ToInt64(q.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        foreach (var r in records)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO extracted_records(id, project_id, file_id, source_file, page, row_number, source_col, target_col, field,
                    original, confidence, doubts_json, status, corrected, leave_empty, selected, seq, bbox_json)
                VALUES ($id, $p, $f, $sf, $pg, $row, $sc, $tc, $fd, $o, $cf, $d, $st, $cr, $le, 0, $seq, $bb)
                """;
            ins.Parameters.AddWithValue("$id", r.Id.ToString());
            ins.Parameters.AddWithValue("$p", projectId.ToString());
            ins.Parameters.AddWithValue("$f", (object?)fileId?.ToString() ?? DBNull.Value);
            ins.Parameters.AddWithValue("$sf", r.SourceFile);
            ins.Parameters.AddWithValue("$pg", (object?)r.Page ?? DBNull.Value);
            ins.Parameters.AddWithValue("$row", (object?)r.Row ?? DBNull.Value);
            ins.Parameters.AddWithValue("$sc", (object?)r.Column ?? DBNull.Value);
            ins.Parameters.AddWithValue("$tc", (object?)r.TargetColumn ?? DBNull.Value);
            ins.Parameters.AddWithValue("$fd", r.FieldName);
            ins.Parameters.AddWithValue("$o", r.Value.Original);
            ins.Parameters.AddWithValue("$cf", (object?)r.Confidence ?? DBNull.Value);
            ins.Parameters.AddWithValue("$d", JsonSerializer.Serialize(r.Doubts));
            ins.Parameters.AddWithValue("$st", r.Status.ToString());
            ins.Parameters.AddWithValue("$cr", (object?)r.CorrectedText ?? DBNull.Value);
            ins.Parameters.AddWithValue("$le", r.LeaveEmpty ? 1 : 0);
            ins.Parameters.AddWithValue("$seq", ++seq);
            ins.Parameters.AddWithValue("$bb", r.Box is null ? DBNull.Value : JsonSerializer.Serialize(r.Box));
            ins.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>Atualiza status/correção/destino de um registro (o original nunca é regravado).</summary>
    public void UpdateState(ExtractedRecord r)
    {
        ArgumentNullException.ThrowIfNull(r);
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE extracted_records SET status = $st, corrected = $cr, leave_empty = $le, target_col = $tc WHERE id = $id";
        cmd.Parameters.AddWithValue("$st", r.Status.ToString());
        cmd.Parameters.AddWithValue("$cr", (object?)r.CorrectedText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$le", r.LeaveEmpty ? 1 : 0);
        cmd.Parameters.AddWithValue("$tc", (object?)r.TargetColumn ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", r.Id.ToString());
        cmd.ExecuteNonQuery();
    }

    public void SaveSelection(Guid projectId, IReadOnlySet<Guid> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        using (var clear = c.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "UPDATE extracted_records SET selected = 0 WHERE project_id = $p";
            clear.Parameters.AddWithValue("$p", projectId.ToString());
            clear.ExecuteNonQuery();
        }

        foreach (var id in selected)
        {
            using var set = c.CreateCommand();
            set.Transaction = tx;
            set.CommandText = "UPDATE extracted_records SET selected = 1 WHERE id = $id";
            set.Parameters.AddWithValue("$id", id.ToString());
            set.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public IReadOnlyList<StoredRecord> Load(Guid projectId)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, file_id, source_file, page, row_number, source_col, target_col, field, original, confidence,
                   doubts_json, status, corrected, leave_empty, selected, bbox_json
            FROM extracted_records WHERE project_id = $p ORDER BY seq
            """;
        cmd.Parameters.AddWithValue("$p", projectId.ToString());
        using var r = cmd.ExecuteReader();
        var list = new List<StoredRecord>();
        while (r.Read())
        {
            var doubts = JsonSerializer.Deserialize<List<Doubt>>(r.GetString(10)) ?? [];
            var rec = new ExtractedRecord(Guid.Parse(r.GetString(0)), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetString(7), ValueClassifier.Classify(r.GetString(8)),
                r.IsDBNull(9) ? null : r.GetDouble(9), doubts, r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(15) ? null : JsonSerializer.Deserialize<CellBox>(r.GetString(15)));
            rec.RestoreState(Enum.Parse<RecordStatus>(r.GetString(11)), r.IsDBNull(12) ? null : r.GetString(12), r.GetInt64(13) == 1);
            list.Add(new StoredRecord(rec, r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)), r.GetInt64(14) == 1));
        }

        return list;
    }

    public void DeleteForFile(Guid projectId, Guid fileId)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM extracted_records WHERE project_id = $p AND file_id = $f";
        cmd.Parameters.AddWithValue("$p", projectId.ToString());
        cmd.Parameters.AddWithValue("$f", fileId.ToString());
        cmd.ExecuteNonQuery();
    }
}
