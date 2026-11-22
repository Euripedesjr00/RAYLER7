using Rayler7.Core.Extraction;

namespace Rayler7.Core.Tests;

public class DoubtDetectorTests
{
    private readonly DoubtDetector _d = new();

    [Fact]
    public void Leitura_clara_nao_gera_duvida()
    {
        var doubts = _d.Inspect(new OcrReading("12,50", [new('1', .99), new('2', .99), new(',', .99), new('5', .99), new('0', .99)], .99, "12,50"));
        Assert.Empty(doubts);
    }

    [Fact]
    public void Zero_pouco_confiavel_sugere_oito_sem_corrigir()
    {
        var reading = new OcrReading("1,08", [new('1', .99), new(',', .99), new('0', .60), new('8', .99)], .9, null);

        var doubts = _d.Inspect(reading);

        var d = Assert.Single(doubts, x => x.Kind == DoubtKind.AmbiguousCharacter);
        Assert.Equal("1,08", d.IdentifiedValue);
        Assert.Equal("1,88", d.SuggestedValue);
    }

    [Fact]
    public void Motores_que_discordam_geram_duvida()
    {
        var doubts = _d.Inspect(new OcrReading("1,08", null, .95, "1,80"));

        var d = Assert.Single(doubts, x => x.Kind == DoubtKind.EngineDisagreement);
        Assert.Equal("1,80", d.SuggestedValue);
    }

    [Fact]
    public void Confianca_baixa()
    {
        var doubts = _d.Inspect(new OcrReading("17", null, .5, null));
        Assert.Contains(doubts, x => x.Kind == DoubtKind.LowConfidence);
    }

    [Fact]
    public void Letra_em_coluna_numerica()
    {
        var doubts = _d.Inspect(new OcrReading("1O,5", null, .95, null), new ColumnExpectation(true, null));

        var d = Assert.Single(doubts, x => x.Kind == DoubtKind.TypeMismatch);
        Assert.Equal("10,5", d.SuggestedValue);
    }

    [Fact]
    public void Casas_decimais_diferentes_da_coluna()
    {
        var doubts = _d.Inspect(new OcrReading("12,5", null, .99, null), new ColumnExpectation(true, 2));
        Assert.Contains(doubts, x => x.Kind == DoubtKind.DecimalPlacesMismatch);
    }

    [Fact]
    public void Ilegivel()
    {
        var doubts = _d.Inspect(new OcrReading(string.Empty, null, null, null));
        Assert.Equal(DoubtKind.Illegible, Assert.Single(doubts).Kind);
    }
}
