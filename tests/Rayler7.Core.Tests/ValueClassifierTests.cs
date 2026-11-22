using Rayler7.Core.Values;

namespace Rayler7.Core.Tests;

/// <summary>Testes de preservação numérica exigidos pela especificação (seção 79).</summary>
public class ValueClassifierTests
{
    [Theory]
    [InlineData("12,50", ValueKind.Decimal, DecimalSeparator.Comma, 2, "12.50")]
    [InlineData("12.50", ValueKind.Decimal, DecimalSeparator.Dot, 2, "12.50")]
    [InlineData("0,005", ValueKind.Decimal, DecimalSeparator.Comma, 3, "0.005")]
    [InlineData("0.005", ValueKind.Decimal, DecimalSeparator.Dot, 3, "0.005")]
    [InlineData("10,00", ValueKind.Decimal, DecimalSeparator.Comma, 2, "10.00")]
    [InlineData("-3,75", ValueKind.Decimal, DecimalSeparator.Comma, 2, "-3.75")]
    [InlineData("0", ValueKind.Integer, DecimalSeparator.None, 0, "0")]
    [InlineData("-42", ValueKind.Integer, DecimalSeparator.None, 0, "-42")]
    [InlineData("1.234.567", ValueKind.Integer, DecimalSeparator.None, 0, "1234567")]
    [InlineData("1.234,56", ValueKind.Decimal, DecimalSeparator.Comma, 2, "1234.56")]
    [InlineData("1,234.56", ValueKind.Decimal, DecimalSeparator.Dot, 2, "1234.56")]
    [InlineData("3,14159265358979", ValueKind.Decimal, DecimalSeparator.Comma, 14, "3.14159265358979")]
    public void Numeros_preservam_original_e_interpretam_em_copia(string original, ValueKind kind, DecimalSeparator sep, int places, string invariant)
    {
        var v = ValueClassifier.Classify(original);

        Assert.Equal(original, v.Original);
        Assert.Equal(kind, v.Kind);
        Assert.Equal(sep, v.Separator);
        Assert.Equal(places, v.DecimalPlaces);
        Assert.Equal(decimal.Parse(invariant, System.Globalization.CultureInfo.InvariantCulture), v.Interpreted);
    }

    [Theory]
    [InlineData("001")]
    [InlineData("00025")]
    [InlineData("0123")]
    public void Zeros_a_esquerda_viram_codigo_e_nao_sao_convertidos(string original)
    {
        var v = ValueClassifier.Classify(original);

        Assert.Equal(ValueKind.Code, v.Kind);
        Assert.Equal(original, v.Original);
        Assert.Null(v.Interpreted);
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("1,234")]
    [InlineData("12.500")]
    public void Separador_ambiguo_nao_e_decidido_sozinho(string original)
    {
        var v = ValueClassifier.Classify(original);

        Assert.Equal(ValueKind.AmbiguousNumber, v.Kind);
        Assert.Null(v.Interpreted);
        Assert.NotEmpty(v.Warnings);
        Assert.Equal(original, v.Original);
    }

    [Fact]
    public void Dica_da_coluna_resolve_ambiguidade_sem_alterar_original()
    {
        var v = ValueClassifier.Classify("1,234", DecimalSeparator.Comma);

        Assert.Equal(ValueKind.Decimal, v.Kind);
        Assert.Equal(1.234m, v.Interpreted);
        Assert.Equal("1,234", v.Original);
    }

    [Theory]
    [InlineData("25/12/2024", 2024, 12, 25)]
    [InlineData("2024-03-01", 2024, 3, 1)]
    [InlineData("5/6/24", 2024, 6, 5)]
    public void Datas_sao_reconhecidas_sem_reformatar(string original, int y, int m, int d)
    {
        var v = ValueClassifier.Classify(original);

        Assert.Equal(ValueKind.Date, v.Kind);
        Assert.Equal(new DateOnly(y, m, d), v.InterpretedDate);
        Assert.Equal(original, v.Original);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Celula_vazia_permanece_vazia(string? original)
    {
        var v = ValueClassifier.Classify(original);

        Assert.Equal(ValueKind.Empty, v.Kind);
        Assert.Equal(string.Empty, v.Original);
        Assert.Null(v.Interpreted);
    }

    [Fact]
    public void Espacos_sao_mantidos_no_original()
    {
        var v = ValueClassifier.Classify(" 12,50 ");

        Assert.Equal(" 12,50 ", v.Original);
        Assert.Equal(12.50m, v.Interpreted);
        Assert.NotEmpty(v.Warnings);
    }

    [Fact]
    public void Valor_muito_grande_nao_perde_original()
    {
        var huge = "123456789012345678901234567890123,5";
        var v = ValueClassifier.Classify(huge);

        Assert.Equal(huge, v.Original);
        Assert.Null(v.Interpreted);
        Assert.NotEmpty(v.Warnings);
    }

    [Theory]
    [InlineData("=SOMA(A1:A3)")]
    [InlineData("Testemunha")]
    [InlineData("12,5 kg")]
    [InlineData("31/02/2024")]
    public void Outros_valores_sao_texto(string original)
    {
        var v = ValueClassifier.Classify(original);

        Assert.Equal(ValueKind.Text, v.Kind);
        Assert.Equal(original, v.Original);
    }
}
