using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Rayler7.App.Services;
using Rayler7.App.ViewModels;

namespace Rayler7.App;

public partial class App
{
    private static string? _logPath;

    /// <summary>Registra um erro tratado (ex.: arquivo que falhou no lote) para diagnóstico, sem dados do usuário.</summary>
    public static void LogError(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        try
        {
            if (_logPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath, $"{DateTimeOffset.Now:O} {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = pt;
        CultureInfo.DefaultThreadCurrentUICulture = pt;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(pt.IetfLanguageTag)));

        DispatcherUnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogError(args.Exception);
            args.SetObserved();
        };
        Controls.WheelForwarding.Register();
        try
        {
            var services = new AppServices();
            _logPath = Path.Combine(services.DataRoot, "logs", "erros.log");
            var window = new MainWindow { DataContext = new MainViewModel(services, new Dialogs()) };
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            MessageBox.Show($"O RAYLER7 não conseguiu abrir o banco de dados local.\n\n{ex.Message}", "RAYLER7", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Registra o erro sem dados do usuário e mantém o programa aberto.
        LogError(e.Exception);

        MessageBox.Show($"Ocorreu um erro inesperado. Seu trabalho salvo não foi afetado.\n\n{e.Exception.Message}", "RAYLER7", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
