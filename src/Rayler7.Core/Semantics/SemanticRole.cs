using System.Text.RegularExpressions;

namespace Rayler7.Core.Semantics;

/// <summary>O que uma coluna representa, deduzido do nome do cabeçalho (ex.: "Peso (kg)" → peso).</summary>
public enum SemanticRole
{
    Generic,
    Id,
    Name,
    Document,
    Date,
    Time,
    Vehicle,
    Plate,
    Quantity,
    Weight,
    Measurement,
    Price,
    Percentage,
    Code,
    Treatment,
    Repetition,
    Block,
    Address,
    City,
    Status,
    Notes,
}

public static partial class SemanticRoles
{
    // Ordem importa: o primeiro papel que casar vence (ex.: "data de coleta" é data, não código).
    private static readonly (SemanticRole Role, string[] Words)[] Keywords =
    [
        (SemanticRole.Document, ["cpf", "cnpj", "documento", "rg"]),
        (SemanticRole.Plate, ["placa", "chassi", "renavam"]),
        (SemanticRole.Vehicle, ["veiculo", "automovel", "carro", "moto", "motocicleta", "transporte", "frota"]),
        (SemanticRole.Date, ["data", "dt", "nascimento", "coleta", "emissao", "vencimento", "dia"]),
        (SemanticRole.Time, ["hora", "horario", "duracao"]),
        (SemanticRole.Treatment, ["tratamento", "trat", "grupo", "protocolo", "dose"]),
        (SemanticRole.Repetition, ["repeticao", "rep", "replica", "repeticoes"]),
        (SemanticRole.Block, ["bloco", "parcela", "talhao", "setor"]),
        (SemanticRole.Weight, ["peso", "massa", "kg", "gramas"]),
        (SemanticRole.Measurement, ["altura", "comprimento", "largura", "diametro", "temperatura", "umidade", "mot", "media", "medida", "ph", "area", "volume", "cm", "mm"]),
        (SemanticRole.Price, ["preco", "valor", "custo", "salario", "total", "r"]),
        (SemanticRole.Percentage, ["percentual", "porcentagem", "taxa"]),
        (SemanticRole.Quantity, ["quantidade", "qtd", "qtde", "numero", "num", "nro", "n", "unidades", "contagem"]),
        (SemanticRole.Name, ["nome", "colaborador", "funcionario", "cliente", "paciente", "aluno", "responsavel", "motorista", "operador", "individuo", "especie", "pessoa"]),
        (SemanticRole.Address, ["endereco", "rua", "av", "avenida", "bairro", "cep", "logradouro"]),
        (SemanticRole.City, ["cidade", "municipio", "uf", "estado", "localidade"]),
        (SemanticRole.Status, ["status", "situacao", "condicao", "resultado", "categoria", "tipo"]),
        (SemanticRole.Notes, ["observacoes", "observacao", "obs", "nota", "notas", "comentario", "detalhes", "descricao"]),
        (SemanticRole.Id, ["id", "identificacao", "identificador", "matricula", "registro"]),
        (SemanticRole.Code, ["codigo", "cod", "chave", "amostra", "lote", "serie", "ref"]),
    ];

    public static SemanticRole Infer(string? header)
    {
        var words = TextNorm.Words(header);
        if (words.Length == 0)
        {
            return SemanticRole.Generic;
        }

        if (header!.Contains('%', StringComparison.Ordinal))
        {
            return SemanticRole.Percentage;
        }

        if (header.Contains("R$", StringComparison.Ordinal))
        {
            return SemanticRole.Price;
        }

        foreach (var (role, keys) in Keywords)
        {
            // Palavras de 1 letra ("n", "r") só contam quando são o cabeçalho inteiro.
            if (words.Any(w => keys.Contains(w) && (w.Length > 1 || words.Length == 1)))
            {
                return role;
            }
        }

        return SemanticRole.Generic;
    }

    /// <summary>Sinônimos usados para reconhecer a mesma informação com outro nome (ex.: "automóvel" → veículo).</summary>
    public static IReadOnlyList<string> Synonyms(SemanticRole role) =>
        Keywords.FirstOrDefault(k => k.Role == role).Words ?? [];

    public static bool ExpectsNumber(SemanticRole role) =>
        role is SemanticRole.Weight or SemanticRole.Measurement or SemanticRole.Price or SemanticRole.Percentage or SemanticRole.Quantity;

    public static string Label(SemanticRole role) => role switch
    {
        SemanticRole.Id => "Identificador",
        SemanticRole.Name => "Nome",
        SemanticRole.Document => "Documento (CPF/CNPJ/RG)",
        SemanticRole.Date => "Data",
        SemanticRole.Time => "Hora",
        SemanticRole.Vehicle => "Veículo",
        SemanticRole.Plate => "Placa",
        SemanticRole.Quantity => "Quantidade",
        SemanticRole.Weight => "Peso",
        SemanticRole.Measurement => "Medida",
        SemanticRole.Price => "Valor (R$)",
        SemanticRole.Percentage => "Percentual",
        SemanticRole.Code => "Código",
        SemanticRole.Treatment => "Tratamento",
        SemanticRole.Repetition => "Repetição",
        SemanticRole.Block => "Bloco/Parcela",
        SemanticRole.Address => "Endereço",
        SemanticRole.City => "Cidade/UF",
        SemanticRole.Status => "Situação/Categoria",
        SemanticRole.Notes => "Observações",
        _ => "Geral",
    };
}
