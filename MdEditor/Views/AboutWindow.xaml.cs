using System.Reflection;
using System.Windows;

namespace MdEditor.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {GetDisplayVersion()}";
    }

    // Lit <Version> du csproj, seule source de verite. InformationalVersion la reprend telle quelle
    // (le suffixe "+<sha git>" du SDK est desactive via IncludeSourceRevisionInInformationalVersion),
    // avec repli sur la version d'assembly, qui est toujours renseignee.
    private static string GetDisplayVersion()
    {
        var assembly = typeof(AboutWindow).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? string.Empty;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
