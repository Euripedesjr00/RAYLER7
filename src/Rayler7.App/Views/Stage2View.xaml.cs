using System.ComponentModel;
using System.Windows;
using Rayler7.App.ViewModels;

namespace Rayler7.App.Views;

public partial class Stage2View
{
    public Stage2View()
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
        if (sender is Stage2ViewModel vm && e.PropertyName == nameof(Stage2ViewModel.Error))
        {
            ErrorPanel.Visibility = string.IsNullOrEmpty(vm.Error) ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is Stage2ViewModel vm && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            vm.AddPaths(files.Where(File.Exists));
        }
    }
}
