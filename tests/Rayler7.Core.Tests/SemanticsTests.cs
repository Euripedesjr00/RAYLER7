using Rayler7.Core.Extraction;
using Rayler7.Core.Mapping;
using Rayler7.Core.Semantics;

namespace Rayler7.Core.Tests;

public class SemanticsTests
{
    private static SourceTable Table(string file, params string[][] rows) =>
        new(file, null, null, rows.Select(r => (IReadOnlyList<SourceCell>)r.Select(t => new SourceCell(t)).ToList()).ToList(), []);

    private static IReadOnlyList<IReadOnlyList<string>> Rows(params string[][] rows) => rows;

    [Fact]
    public void Cabecalho_abaixo_de_titulo_e_metadados_e_encontrado()
    {
        var rows = Rows(
            ["Ficha de campo - Ensaio 2026", "", "", ""],
            ["Local: Fazenda Boa Vista", "", "", ""],
            ["", "", "", ""],
            ["Tratamento", "Repetição", "Altura (cm)", "Peso (kg)"],
            ["T1", "001", "12,50", "3,20"],
            ["T2", "002", "8,75", "2,95"]);

        Assert.Equal(3, HeaderDetector.Detect(rows));
    }

    [Fact]
    public void Tabela_so_com_dados_nao_tem_cabecalho()
    {
        Assert.Null(HeaderDetector.Detect(Rows(["T1", "001", "12,50"], ["T2", "002", "8,75"])));
    }

    [Theory]
    [InlineData("Peso (kg)", SemanticRole.Weight)]
    [InlineData("Data da coleta", SemanticRole.Date)]
    [InlineData("Veículo", SemanticRole.Vehicle)]
    [InlineData("Nome do motorista", SemanticRole.Name)]
    [InlineData("Placa", SemanticRole.Plate)]
    [InlineData("Taxa (%)", SemanticRole.Percentage)]
    [InlineData("Qualquer coisa", SemanticRole.Generic)]
    public void Papel_da_coluna_pelo_nome(string header, SemanticRole role) => Assert.Equal(role, SemanticRoles.Infer(header));

    [Fact]
    public void Sinonimo_associa_automovel_a_veiculo_e_kg_a_peso()
    {
        string[] template = ["Nome", "Veículo", "Peso (kg)"];
        var plan = ColumnMatcher.Match(["Massa", "Automóvel", "Motorista"],
            [["72,5"], ["Carro"], ["Ana"]], template, []);

        Assert.True(plan.ByName);
        Assert.Equal(3, plan.For(0)!.TargetColumn);
        Assert.Equal(MatchReason.Synonym, plan.For(0)!.Reason);
        Assert.Equal(2, plan.For(1)!.TargetColumn);
        Assert.Equal(1, plan.For(2)!.TargetColumn);
    }

    [Fact]
    public void Texto_nao_vai_para_coluna_de_peso_mesmo_com_nome_parecido()
    {
        // "Peso obs" contém "peso", mas os valores são texto: a associação com "Peso (kg)" é derrubada.
        var plan = ColumnMatcher.Match(["Peso obs"], [["sem balança", "não pesado"]], ["Peso (kg)"], []);

        Assert.Empty(plan.Matches);
    }

    [Fact]
    public void Nome_dado_nas_instrucoes_associa_a_coluna()
    {
        var mapping = new ColumnMapping(2, "Var2", "MOT", true, new Dictionary<string, string>(), MappingSource.Instruction);
        var plan = ColumnMatcher.Match(["Amostra", "MOT"], [["A1"], ["3,4"]], ["Amostra", "Var2"], [mapping]);

        Assert.Equal(2, plan.For(1)!.TargetColumn);
        Assert.Equal(MatchReason.Instruction, plan.For(1)!.Reason);
    }

    [Fact]
    public void Construtor_pula_titulo_e_cabecalho_repetido_e_associa_colunas_reordenadas()
    {
        string[] template = ["Tratamento", "Repetição", "Altura (cm)", "Peso (kg)"];
        var t = Table("dados.pdf",
            ["Relatório de campo", "", "", ""],
            ["Peso", "Trat.", "Altura", "Rep"],
            ["3,20", "T1", "12,50", "001"],
            ["Peso", "Trat.", "Altura", "Rep"],
            ["2,95", "T2", "8,75", "002"]);

        var r = RecordBuilder.Build(t, template, []);

        Assert.True(r.MatchedByHeader);
        Assert.DoesNotContain(r.Records, x => x.Value.Original is "Peso" or "Relatório de campo");
        Assert.Equal("D", r.Records.First(x => x.Value.Original == "3,20").TargetColumn);
        Assert.Equal("C", r.Records.First(x => x.Value.Original == "12,50").TargetColumn);
        Assert.Equal("B", r.Records.First(x => x.Value.Original == "001").TargetColumn);
        Assert.Equal("001", r.Records.First(x => x.TargetColumn == "B").Value.Original); // zero à esquerda preservado
        Assert.Contains(r.Warnings, w => w.Contains("título", StringComparison.Ordinal));
        Assert.Contains(r.Warnings, w => w.Contains("repetido", StringComparison.Ordinal));
    }

    [Fact]
    public void Coluna_sem_nome_reconhecido_vai_pela_posicao_com_duvida()
    {
        string[] template = ["Tratamento", "Repetição", "Altura (cm)"];
        var t = Table("dados.csv", ["Tratamento", "Rep", "X9"], ["T1", "001", "12,50"]);

        var r = RecordBuilder.Build(t, template, []);

        var altura = r.Records.Single(x => x.Value.Original == "12,50");
        Assert.Equal("C", altura.TargetColumn);
        Assert.Contains(altura.Doubts, d => d.Kind == DoubtKind.PositionalMatch);
    }

    [Fact]
    public void Ficha_campo_valor_vira_linhas_da_planilha()
    {
        string[] template = ["Nome", "Placa", "Peso (kg)"];
        var t = new SourceTable("ficha.png", 1, "Imagem",
        [
            [new SourceCell("Nome:"), new SourceCell("Maria Souza")],
            [new SourceCell("Placa:"), new SourceCell("ABC-1234")],
            [new SourceCell("Peso: 72,5")],
            [],
            [new SourceCell("Nome:"), new SourceCell("João Lima")],
            [new SourceCell("Placa:"), new SourceCell("XYZ-9876")],
            [new SourceCell("Peso:"), new SourceCell("81,0")],
        ], []);

        var r = RecordBuilder.Build(t, template, []);

        Assert.Equal(6, r.Records.Count);
        Assert.Equal(["Maria Souza", "João Lima"], r.Records.Where(x => x.TargetColumn == "A").Select(x => x.Value.Original));
        Assert.Equal(["ABC-1234", "XYZ-9876"], r.Records.Where(x => x.TargetColumn == "B").Select(x => x.Value.Original));
        Assert.Equal(["72,5", "81,0"], r.Records.Where(x => x.TargetColumn == "C").Select(x => x.Value.Original));
        Assert.Contains(r.Warnings, w => w.Contains("Campo: Valor", StringComparison.Ordinal));
    }

    [Fact]
    public void Ficha_sem_linha_em_branco_separa_quando_o_rotulo_repete()
    {
        var t = Table("fichas.txt", ["Nome: Ana"], ["Placa: AAA-1111"], ["Nome: Bia"], ["Placa: BBB-2222"]);

        var converted = KeyValueForms.TryConvert(t);

        Assert.NotNull(converted);
        Assert.Equal(3, converted.Rows.Count); // cabeçalho + 2 fichas
        Assert.Equal("Bia", converted.Rows[2][0].Text);
    }

    [Fact]
    public void Tabela_comum_nao_e_confundida_com_ficha()
    {
        Assert.Null(KeyValueForms.TryConvert(Table("a.csv", ["Hora", "Valor"], ["10:30", "5"], ["11:00", "7"])));
    }

    [Fact]
    public void Linha_repetida_vira_duvida_sem_apagar_nada()
    {
        string[] template = ["Tratamento", "Repetição", "Altura (cm)"];
        var t = Table("dados.csv", ["Tratamento", "Repetição", "Altura (cm)"], ["T1", "001", "12,50"], ["T2", "002", "8,75"], ["T1", "001", "12,50"]);
        var records = RecordBuilder.Build(t, template, []).Records;
        var existing = new List<IReadOnlyDictionary<int, string>> { new Dictionary<int, string> { [1] = "T2", [2] = "002", [3] = "8,75" } };

        var flagged = DuplicateDetector.Flag(records, existing);

        Assert.Equal(2, flagged); // T2 (já existe no modelo) e a 2ª ocorrência de T1
        Assert.Equal(9, records.Count);
        Assert.Equal(2, records.Count(x => x.Doubts.Any(d => d.Kind == DoubtKind.DuplicateRow)));
        Assert.DoesNotContain(records.Where(x => x.Row == 2), x => x.Doubts.Any(d => d.Kind == DoubtKind.DuplicateRow));
    }
}
