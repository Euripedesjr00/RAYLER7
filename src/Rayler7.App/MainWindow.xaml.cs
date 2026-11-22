using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Rayler7.App.ViewModels;

namespace Rayler7.App;

public partial class MainWindow
{
    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += OnVmChanged;
                UpdateActive(vm);
            }
        };
    }

    /// <summary>
    /// Ajusta a janela à área útil da tela (descontando a barra de tarefas), para que a barra de
    /// título nunca fique fora da tela em notebooks ou escalas de 125%/150%.
    /// </summary>
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        if (area.Width < Width || area.Height < Height)
        {
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = area.Left;
            Top = area.Top;
            WindowState = WindowState.Maximized;
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MainViewModel vm && e.PropertyName is nameof(MainViewModel.Stage) or nameof(MainViewModel.CurrentPage))
        {
            UpdateActive(vm);
        }
    }

    /// <summary>Destaca a aba e a etapa atuais (sublinhado verde, como no layout de referência).</summary>
    private void UpdateActive(MainViewModel vm)
    {
        NavCover.Tag = vm.Stage == AppStage.Cover ? "active" : null;
        NavS1.Tag = vm.Stage == AppStage.Stage1 ? "active" : null;
        NavS2.Tag = vm.Stage == AppStage.Stage2 ? "active" : null;
        NavS3.Tag = vm.Stage == AppStage.Stage3 ? "active" : null;
        SetPill(Pill1, vm.Stage == AppStage.Stage1);
        SetPill(Pill2, vm.Stage == AppStage.Stage2);
        SetPill(Pill3, vm.Stage == AppStage.Stage3);
    }

    private void SetPill(Button b, bool active) =>
        b.Style = (Style)FindResource(active ? "SmallPrimaryButton" : "SmallOutlineButton");
}
