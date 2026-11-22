using Rayler7.Core.Audit;
using Rayler7.Core.Extraction;
using Rayler7.Core.Selection;
using Rayler7.Core.Values;

namespace Rayler7.Core.Tests;

/// <summary>Testes obrigatórios do botão "Selecionar todos os dados" (seção 78 da especificação).</summary>
public class ReviewSessionTests
{
    private static readonly Guid Project = Guid.NewGuid();

    private static ExtractedRecord Rec(int i, string value = "1") =>
        new(Guid.NewGuid(), $"foto_{i:000}.jpg", 1, i, "A", "Altura", ValueClassifier.Classify(value), 0.99);

    private static ReviewSession Session(int n)
    {
        var s = new ReviewSession();
        s.AddRecords(Enumerable.Range(1, n).Select(i => Rec(i)));
        return s;
    }

    [Fact] // 1
    public void Poucos_registros_selecionar_todos()
    {
        var s = Session(3);
        s.SelectAll(SelectionScope.AllRecords);

        var sum = s.Summary();
        Assert.Equal(3, sum.TotalExtracted);
        Assert.Equal(3, sum.Selected);
        Assert.Equal(0, sum.NotSelected);
        Assert.True(sum.AllEligibleSelected);
    }

    [Fact] // 2 e 3
    public void Milhares_de_registros_em_varias_paginas_sao_todos_selecionados()
    {
        var s = Session(5000);
        Assert.Equal(100, s.PageCount(50));

        s.SelectAll(SelectionScope.AllRecords);

        Assert.Equal(5000, s.Summary().Selected);
        Assert.All(s.Page(99, 50), r => Assert.True(s.IsSelected(r.Id)));
        Assert.All(s.Page(0, 50), r => Assert.True(s.IsSelected(r.Id)));
    }

    [Fact] // 4
    public void Com_filtro_o_escopo_e_explicito()
    {
        var s = new ReviewSession();
        var a = Enumerable.Range(1, 10).Select(i => Rec(i, "10")).ToList();
        var b = Enumerable.Range(11, 5).Select(i => Rec(i, "20")).ToList();
        s.AddRecords(a.Concat(b));
        s.SetFilter(r => r.Value.Original == "20");

        s.SelectAll(SelectionScope.FilteredOnly);
        Assert.Equal(5, s.Summary().Selected);
        Assert.All(a, r => Assert.False(s.IsSelected(r.Id)));

        s.SelectAll(SelectionScope.AllRecords);
        Assert.Equal(15, s.Summary().Selected);
    }

    [Fact] // 5
    public void Desmarcar_todos()
    {
        var s = Session(200);
        s.SelectAll(SelectionScope.AllRecords);
        s.DeselectAll(SelectionScope.AllRecords);

        Assert.Equal(0, s.Summary().Selected);
        Assert.Equal(200, s.Summary().NotSelected);
    }

    [Fact] // 6
    public void Novos_registros_entram_desmarcados_e_sao_contados()
    {
        var s = Session(10);
        s.SelectAll(SelectionScope.AllRecords);
        s.AddRecords(Enumerable.Range(11, 4).Select(i => Rec(i)));

        var sum = s.Summary();
        Assert.Equal(14, sum.TotalExtracted);
        Assert.Equal(10, sum.Selected);
        Assert.Equal(4, sum.NewUnselectedSinceSelectAll);
        Assert.False(sum.AllEligibleSelected);

        s.SelectAll(SelectionScope.AllRecords);
        Assert.Equal(14, s.Summary().Selected);
        Assert.Equal(0, s.Summary().NewUnselectedSinceSelectAll);
    }

    [Fact] // 7
    public void Rejeitados_nao_entram_na_selecao()
    {
        var audit = new InMemoryAuditLog();
        var s = Session(6);
        s.Records[0].Reject("ana", Project, audit, "duplicado");
        s.Records[1].Reject("ana", Project, audit);

        s.SelectAll(SelectionScope.AllRecords);
        var sum = s.Summary();

        Assert.Equal(4, sum.Selected);
        Assert.Equal(2, sum.Rejected);
        Assert.True(sum.AllEligibleSelected);
        Assert.Throws<InvalidOperationException>(() => s.Select(s.Records[0].Id));
    }

    [Fact] // 7b
    public void Rejeitar_depois_de_selecionado_remove_da_selecao()
    {
        var audit = new InMemoryAuditLog();
        var s = Session(3);
        s.SelectAll(SelectionScope.AllRecords);
        s.Records[2].Reject("ana", Project, audit);
        s.RefreshAfterStatusChange();

        Assert.Equal(2, s.Summary().Selected);
    }

    [Fact] // 8
    public void Navegar_entre_paginas_nao_perde_selecao()
    {
        var s = Session(120);
        var p2 = s.Page(2, 50);
        s.Select(p2[0].Id);
        s.Select(s.Page(0, 50)[3].Id);

        _ = s.Page(1, 50);
        _ = s.Page(0, 50);
        s.SetFilter(r => false);
        s.SetFilter(null);

        Assert.True(s.IsSelected(p2[0].Id));
        Assert.Equal(2, s.Summary().Selected);
    }

    [Fact] // 9
    public void Alteracao_manual_depois_de_selecionar_todos()
    {
        var s = Session(50);
        s.SelectAll(SelectionScope.AllRecords);
        s.Deselect(s.Records[10].Id);

        var sum = s.Summary();
        Assert.Equal(49, sum.Selected);
        Assert.False(sum.AllEligibleSelected);
        s.Toggle(s.Records[10].Id);
        Assert.True(s.Summary().AllEligibleSelected);
    }

    [Fact] // 10
    public void Preenchimento_usa_apenas_os_selecionados()
    {
        var s = Session(20);
        s.Select(s.Records[3].Id);
        s.Select(s.Records[7].Id);

        var chosen = s.SelectedRecords();
        Assert.Equal(2, chosen.Count);
        Assert.Equal([s.Records[3].Id, s.Records[7].Id], chosen.Select(r => r.Id));
    }

    [Fact]
    public void Contadores_de_status()
    {
        var s = new ReviewSession();
        s.AddRecords([Rec(1), Rec(2, "1.234"), Rec(3)]);

        var sum = s.Summary();
        Assert.Equal(2, sum.PendingReview);
        Assert.Equal(1, sum.Doubtful);
    }
}
