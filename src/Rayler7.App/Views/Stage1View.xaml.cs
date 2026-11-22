using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using Rayler7.App.ViewModels;

namespace Rayler7.App.Views;

public partial class Stage1View
{
    public Stage1View()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged o)
            {
                o.PropertyChanged -= OnVmChanged;
            }

            if (e.NewValue is INotifyPropertyChanged n)
            {
                n.PropertyChanged += OnVmChanged;
            }
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is Stage1ViewModel vm && e.PropertyName is nameof(Stage1ViewModel.Error) or "")
        {
            ErrorPanel.Visibility = string.IsNullOrEmpty(vm.Error) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    /// <summary>Usa a legenda "A · Cabeçalho" como título da coluna e desliga a interpretação de sublinhados.</summary>
    private void OnAutoColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is Stage1ViewModel { Preview: { } view } && view.Table?.Columns[e.PropertyName] is DataColumn col)
        {
            e.Column.Header = new TextBlock { Text = col.Caption };
            e.Column.MaxWidth = 320;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is Stage1ViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            vm.LoadTemplate(files[0]);
        }
    }
}
