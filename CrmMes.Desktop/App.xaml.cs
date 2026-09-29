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

        base.OnStartup(e);
    }
}
