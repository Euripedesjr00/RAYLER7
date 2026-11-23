using System.ComponentModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Rayler7.App.ViewModels;

namespace Rayler7.App.Views;

public partial class Stage3View
{
    public Stage3View()
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
                if (e.NewValue is Stage3ViewModel vm)
                {
                    ErrorPanel.Visibility = string.IsNullOrEmpty(vm.Error) ? Visibility.Collapsed : Visibility.Visible;
                }
            }
        };
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Stage3ViewModel vm)
        {
            return;
        }

        if (e.PropertyName == nameof(Stage3ViewModel.Error))
        {
            ErrorPanel.Visibility = string.IsNullOrEmpty(vm.Error) ? Visibility.Collapsed : Visibility.Visible;
        }
        else if (e.PropertyName is nameof(Stage3ViewModel.LastExport) or nameof(Stage3ViewModel.ExportCompleted)
                 && vm.ExportCompleted && vm.LastExport is not null)
        {
            // Animação discreta de conclusão (respeita quem desativou animações no Windows).
            var duration = SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(450) : TimeSpan.Zero;
            FinalCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Dispatcher.BeginInvoke(() => FinalCard.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void OnAutoColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (DataContext is Stage3ViewModel { Preview: { } view } && view.Table?.Columns[e.PropertyName] is DataColumn col)
        {
            e.Column.Header = new TextBlock { Text = col.Caption };
            e.Column.MaxWidth = 320;
        }
    }
}
