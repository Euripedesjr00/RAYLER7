using Rayler7.Core.Audit;
using Rayler7.Core.Extraction;
using Rayler7.Core.Mapping;

namespace Rayler7.Core.Tests;

public class RecordBuilderTests
{
    private static readonly string[] Headers = ["Tratamento", "Repetição", "Altura (cm)", "Diâmetro (mm)"];

    private static SourceTable Table(params string[][] rows) =>
        new("dados.csv", null, null, rows.Select(r => (IReadOnlyList<SourceCell>)r.Select(t => new SourceCell(t)).ToList()).ToList(), []);

    private static ColumnMapping Map(int i, bool numeric = false) =>
        new(i, Headers[i - 1], Headers[i - 1], numeric, new Dictionary<string, string>(), MappingSource.Header);

    [Fact]
    public void Associa_por_cabecalho_mesmo_com_ordem_diferente()
    {
        var t = Table(["Altura (cm)", "Tratamento"], ["12,50", "T1"]);

        var r = RecordBuilder.Build(t, Headers, [Map(1), Map(3, true)]);

        Assert.True(r.MatchedByHeader);
        Assert.Equal("C", r.Records.Single(x => x.Value.Original == "12,50").TargetColumn);
        Assert.Equal("A", r.Records.Single(x => x.Value.Original == "T1").TargetColumn);
    }

    [Fact]
    public void Sem_cabecalho_associa_pela_ordem_e_preserva_valores()
    {
        var t = Table(["T1", "001", "12,50", "0.005"]);

        var r = RecordBuilder.Build(t, Headers, []);

        Assert.False(r.HeaderRowSkipped);
        Assert.Equal(["001", "12,50", "0.005"], r.Records.Skip(1).Select(x => x.Value.Original));
        Assert.Equal(["A", "B", "C", "D"], r.Records.Select(x => x.TargetColumn!));
    }

    [Fact]
    public void Coluna_excedente_nao_cria_coluna_nova()
    {
        var t = Table(["T1", "1", "10", "5", "extra"]);

        var r = RecordBuilder.Build(t, Headers, []);

        var extra = r.Records.Single(x => x.Value.Original == "extra");
        Assert.Null(extra.TargetColumn);
        Assert.Contains(extra.Doubts, d => d.Kind == DoubtKind.NoTargetColumn);
    }

    [Fact]
    public void Letra_em_coluna_numerica_vira_duvida()
    {
        var t = Table(["T1", "1", "1O,5", "5"]);

        var r = RecordBuilder.Build(t, Headers, [Map(3, true)]);

        Assert.True(r.Records.Single(x => x.TargetColumn == "C").HasOpenDoubt);
    }

    [Fact]
    public void Planejamento_ignora_rejeitados_e_duvidas_sem_autorizacao()
    {
        var audit = new InMemoryAuditLog();
        var t = Table(["T1", "1", "10", "5"], ["T2", "2", "1.234", "6"]);
        var recs = RecordBuilder.Build(t, Headers, []).Records;
        recs.Single(x => x.Value.Original == "6").Reject("ana", Guid.Empty, audit);

        var plan = FillPlanner.Plan(recs, firstFreeRow: 4, allowOpenDoubts: false);

        Assert.Equal(2, plan.RowsAdded);
        Assert.Equal(1, plan.ExcludedRejected);
        Assert.Equal(1, plan.ExcludedOpenDoubts); // 1.234 é ambíguo
        Assert.Contains(plan.Writes, w => w is { Row: 4, Column: 3, Text: "10" });
        Assert.DoesNotContain(plan.Writes, w => w.Text == "1.234");

        var authorized = FillPlanner.Plan(recs, 4, allowOpenDoubts: true);
        Assert.Contains(authorized.Writes, w => w.Text == "1.234" && w.FlaggedDoubt);
    }

    [Fact]
    public void Correcao_autorizada_e_usada_no_preenchimento()
    {
        var audit = new InMemoryAuditLog();
        var recs = RecordBuilder.Build(Table(["T1", "1", "1,08", "5"]), Headers, []).Records;
        recs.Single(x => x.Value.Original == "1,08").Correct("1,80", "ana", Guid.Empty, audit);

        var plan = FillPlanner.Plan(recs, 3, false);

        Assert.Contains(plan.Writes, w => w.Text == "1,80");
        Assert.Equal("1,08", recs.Single(x => x.TargetColumn == "C").Value.Original);
    }
}
