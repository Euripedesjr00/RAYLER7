using Rayler7.Core.Mapping;

namespace Rayler7.Core.Tests;

public class InstructionParserTests
{
    [Fact]
    public void Exemplo_carro_e_moto_da_especificacao()
    {
        const string text = "Na primeira coluna, o número 1 significa carro e o número 2 significa moto. " +
                            "Na segunda coluna, inserir o nome do participante. " +
                            "Na terceira coluna, inserir o valor encontrado na imagem. " +
                            "Não modificar nenhum valor numérico.";

        var r = InstructionParser.Parse(text);

        var c1 = Assert.Single(r.Proposals, p => p.ColumnIndex == 1);
        Assert.Equal("carro", c1.ValueDictionary["1"]);
        Assert.Equal("moto", c1.ValueDictionary["2"]);
        Assert.Equal("nome do participante", Assert.Single(r.Proposals, p => p.ColumnIndex == 2).Meaning);
        Assert.Equal("valor encontrado na imagem", Assert.Single(r.Proposals, p => p.ColumnIndex == 3).Meaning);
        Assert.Contains("Não modificar valores", r.RecognizedRules);
        Assert.Empty(r.Unrecognized);
    }

    [Fact]
    public void Exemplo_com_letras_e_frase_nao_reconhecida()
    {
        const string text = "A coluna A corresponde ao tratamento; a coluna B corresponde à repetição; " +
                            "a coluna C corresponde à altura das plantas; a coluna D corresponde ao diâmetro. " +
                            "Cada fotografia apresenta os resultados de uma repetição.";

        var r = InstructionParser.Parse(text);

        Assert.Equal("tratamento", r.Proposals.Single(p => p.ColumnIndex == 1).Meaning);
        Assert.Equal("repetição", r.Proposals.Single(p => p.ColumnIndex == 2).Meaning);
        Assert.Equal("altura das plantas", r.Proposals.Single(p => p.ColumnIndex == 3).Meaning);
        Assert.Equal("diâmetro", r.Proposals.Single(p => p.ColumnIndex == 4).Meaning);
        Assert.Single(r.Unrecognized);
    }

    [Fact]
    public void Lista_com_dois_pontos()
    {
        var r = InstructionParser.Parse("Coluna 1: nome do carro\nColuna 2: nome da moto\nColuna 3: idade");

        Assert.Equal(3, r.Proposals.Count);
        Assert.Equal("idade", r.Proposals[2].Meaning);
    }

    [Fact]
    public void Palavra_minuscula_nao_vira_coluna()
    {
        var r = InstructionParser.Parse("Inserir na coluna de peso o valor lido");
        Assert.Empty(r.Proposals);
        Assert.Single(r.Unrecognized);
    }

    [Fact]
    public void Definicoes_conflitantes_viram_ambiguidade()
    {
        var r = InstructionParser.Parse("Coluna 2: peso\nColuna 2: altura");
        Assert.Single(r.Ambiguities);
    }

    [Fact]
    public void Coluna_alem_do_modelo_e_ambiguidade()
    {
        var r = InstructionParser.Parse("Coluna 9: peso", knownColumnCount: 4);
        Assert.Empty(r.Proposals);
        Assert.Single(r.Ambiguities);
    }

    [Fact]
    public void Texto_vazio()
    {
        var r = InstructionParser.Parse("   ");
        Assert.Empty(r.Proposals);
    }
}
