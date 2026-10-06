using Avalonia;
using Avalonia.Headless;
using System.Globalization;
using AegiNext.Desktop.I18n;

[assembly: AvaloniaTestApplication(typeof(AegiNext.Desktop.Ui.Tests.UiTestAppBuilder))]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AegiNext.Desktop.Ui.Tests;

public static class UiTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var directory = Directory.CreateTempSubdirectory("AegiNext-localization-ui-");
        try
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "i18n"), "*.json"))
            {
                File.Copy(file, Path.Combine(directory.FullName, Path.GetFileName(file)));
            }

            File.WriteAllText(Path.Combine(directory.FullName, "custom-french.json"), """
                {
                  "LanguageName": "Français (Canada)",
                  "LanguageID": "fr-CA",
                  "Strings": {
                    "Settings.Settings": "Paramètres",
                    "Preview.Play": "Lire",
                    "Workbench.Save": "Enregistrer"
                  }
                }
                """);
            Localization.Initialize(directory.FullName);
        }
        finally
        {
            directory.Delete(true);
        }

        return AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false });
    }
}
