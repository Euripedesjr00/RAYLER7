namespace Rayler7.App.ViewModels;

/// <summary>Tela honesta para etapas ainda não implementadas (exigência da especificação).</summary>
public sealed class PendingStageViewModel(string eyebrow, string title, string message)
{
    public string Eyebrow { get; } = eyebrow;

    public string Title { get; } = title;

    public string Message { get; } = message;
}
