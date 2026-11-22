using System.Windows;
using Microsoft.Win32;

namespace Rayler7.App.Services;

public interface IDialogs
{
    string? OpenFile(string title, string filter);

    string[] OpenFiles(string title, string filter);

    bool Confirm(string message, string title);

    void Info(string message, string title);
}

public sealed class Dialogs : IDialogs
{
    public string? OpenFile(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, Multiselect = false };
        return d.ShowDialog() == true ? d.FileName : null;
    }

    public string[] OpenFiles(string title, string filter)
    {
        var d = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        return d.ShowDialog() == true ? d.FileNames : [];
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void Info(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
