using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace CrmMes.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // WPF formats bindings (StringFormat=N2, dates, decimals) with en-US unless told otherwise,
        // regardless of the Windows regional settings: a quote total showed as "2,550.90" instead of
        // "2.550,90". Italian formatting everywhere, set once for every element.
        var italian = CultureInfo.GetCultureInfo("it-IT");
        CultureInfo.DefaultThreadCurrentCulture = italian;
        CultureInfo.DefaultThreadCurrentUICulture = italian;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(italian.IetfLanguageTag)));

        // Every error the program didn't handle ends up in the client log (Teleassistenza sends it).
        DispatcherUnhandledException += (_, args) =>
        {
            ClientLog.Error("Errore non gestito", args.Exception);
            MessageBox.Show($"Si è verificato un errore imprevisto: {args.Exception.Message}\n\nL'errore è stato registrato: da Teleassistenza puoi inviarlo all'assistenza.",
                "Nicolò MES", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ClientLog.Error("Errore fatale", exception);
            }
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ClientLog.Error("Operazione in background", args.Exception);
            args.SetObserved();
        };
        ClientLog.Info($"Avvio versione {SupportPackage.ClientVersion}");

        // Dialogs taller than the work area (taskbar / DPI) hid their footer buttons — clamp every window.
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((_, args) =>
            {
                if (args.Source is Window window)
                {
                    FitDialogToWorkArea.Ensure(window);
                }
            }));

        base.OnStartup(e);
    }
}
