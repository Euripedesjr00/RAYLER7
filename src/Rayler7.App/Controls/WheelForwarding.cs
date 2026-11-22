using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Rayler7.App.Controls;

/// <summary>
/// Quando uma tabela chega ao fim da própria rolagem (ou não tem rolagem), a roda do mouse
/// passa a rolar a página. Sem isso, o mouse "trava" ao passar sobre as tabelas.
/// </summary>
public static class WheelForwarding
{
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(DataGrid), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnWheel));

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject grid || e.Handled)
        {
            return;
        }

        var inner = FindChild<ScrollViewer>(grid);
        var atTop = inner is null || inner.VerticalOffset <= 0;
        var atBottom = inner is null || inner.VerticalOffset >= inner.ScrollableHeight;
        if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom))
        {
            return;
        }

        var outer = FindParent<ScrollViewer>(grid);
        if (outer is null)
        {
            return;
        }

        e.Handled = true;
        outer.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }

    private static T? FindParent<T>(DependencyObject child)
        where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(child);
        while (current is not null and not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private static T? FindChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found)
            {
                return found;
            }

            if (FindChild<T>(child) is { } deeper)
            {
                return deeper;
            }
        }

        return null;
    }
}
