using System.Text.Json;
using Rayler7.Core.Mapping;
using Rayler7.Infrastructure.Persistence;

namespace Rayler7.Infrastructure.Projects;

/// <summary>Grava e lê o mapeamento de colunas de um projeto (para continuar o trabalho depois).</summary>
public sealed class MappingStore(Database db)
{
    public void Save(Guid projectId, IEnumerable<ColumnMapping> mappings, bool confirmed)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM column_mappings WHERE project_id = $p";
            del.Parameters.AddWithValue("$p", projectId.ToString());
            del.ExecuteNonQuery();
        }

        foreach (var m in mappings)
        {
            using var ins = c.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO column_mappings(project_id, column_index, header, meaning, expect_numeric, dictionary_json, source)
                VALUES ($p, $i, $h, $m, $n, $d, $s)
                """;
            ins.Parameters.AddWithValue("$p", projectId.ToString());
            ins.Parameters.AddWithValue("$i", m.ColumnIndex);
            ins.Parameters.AddWithValue("$h", (object?)m.Header ?? DBNull.Value);
            ins.Parameters.AddWithValue("$m", m.Meaning);
            ins.Parameters.AddWithValue("$n", m.ExpectNumeric ? 1 : 0);
            ins.Parameters.AddWithValue("$d", JsonSerializer.Serialize(m.ValueDictionary));
            ins.Parameters.AddWithValue("$s", m.Source.ToString());
            ins.ExecuteNonQuery();
        }

        using (var upd = c.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE projects SET mapping_confirmed = $c WHERE id = $p";
            upd.Parameters.AddWithValue("$c", confirmed ? 1 : 0);
            upd.Parameters.AddWithValue("$p", projectId.ToString());
            upd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public (IReadOnlyList<ColumnMapping> Mappings, bool Confirmed) Load(Guid projectId)
    {
        using var c = db.Open();
        var list = new List<ColumnMapping>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT column_index, header, meaning, expect_numeric, dictionary_json, source FROM column_mappings WHERE project_id = $p ORDER BY column_index";
            cmd.Parameters.AddWithValue("$p", projectId.ToString());
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(r.GetString(4)) ?? [];
                list.Add(new ColumnMapping(r.GetInt32(0), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2),
                    r.GetInt64(3) == 1, dict, Enum.Parse<MappingSource>(r.GetString(5))));
            }
        }

        using var q = c.CreateCommand();
        q.CommandText = "SELECT mapping_confirmed FROM projects WHERE id = $p";
        q.Parameters.AddWithValue("$p", projectId.ToString());
        var confirmed = Convert.ToInt64(q.ExecuteScalar() ?? 0L, System.Globalization.CultureInfo.InvariantCulture) == 1;
        return (list, confirmed);
    }
}
