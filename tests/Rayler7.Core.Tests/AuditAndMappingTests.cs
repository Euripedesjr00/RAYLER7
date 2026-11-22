using Rayler7.Core.Audit;
using Rayler7.Core.Extraction;
using Rayler7.Core.Mapping;
using Rayler7.Core.Values;

namespace Rayler7.Core.Tests;

public class AuditAndMappingTests
{
    private static readonly Guid Project = Guid.NewGuid();

    [Fact]
    public void Correcao_preserva_original_e_registra_historico()
    {
        var audit = new InMemoryAuditLog();
        var r = new ExtractedRecord(Guid.NewGuid(), "foto.jpg", 2, 5, "D", "Altura", ValueClassifier.Classify("1,08"), .7);

        r.Correct("1,80", "ana", Project, audit, "conferido na foto");

        Assert.Equal("1,08", r.Value.Original);
        Assert.Equal("1,80", r.EffectiveText);
        Assert.Equal(RecordStatus.CorrectedManually, r.Status);
        var e = Assert.Single(audit.Entries(Project));
        Assert.Equal("1,08", e.OldValue);
        Assert.Equal("1,80", e.NewValue);
        Assert.Equal("ana", e.User);
        Assert.Equal("conferido na foto", e.Reason);
    }

    [Fact]
    public void Valor_ambiguo_nasce_como_duvida()
    {
        var r = new ExtractedRecord(Guid.NewGuid(), "f.pdf", 1, 1, "A", "X", ValueClassifier.Classify("1.234"), 1);
        Assert.True(r.HasOpenDoubt);
    }

    [Fact]
    public void Mapeamento_confirmado_nao_aceita_alteracao_sem_destravar()
    {
        var audit = new InMemoryAuditLog();
        var set = new MappingSet();
        set.Set(new ColumnMapping(1, "Trat", "Tratamento", false, new Dictionary<string, string>(), MappingSource.Header));
        set.Confirm("ana", Project, audit);

        Assert.Throws<InvalidOperationException>(() =>
            set.Set(new ColumnMapping(1, "Trat", "Outro", false, new Dictionary<string, string>(), MappingSource.Instruction)));

        set.Unlock("ana", Project, audit, "corrigir nome");
        set.Set(new ColumnMapping(1, "Trat", "Outro", false, new Dictionary<string, string>(), MappingSource.Manual));
        Assert.Equal("Outro", set.Get(1)!.Meaning);
        Assert.Equal(2, audit.Entries(Project).Count);
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(703, "AAA")]
    public void Letras_de_coluna(int index, string letters)
    {
        Assert.Equal(letters, ColumnLetters.FromIndex(index));
        Assert.Equal(index, ColumnLetters.ToIndex(letters));
    }
}
