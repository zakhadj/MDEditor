using System;
using System.Windows;
using MdEditor.Models;

namespace MdEditor.Services;

public static class ThemeService
{
    /// <summary>
    /// App.xaml merges Styles.xaml at index 0 and the active palette (Light/Dark) at index 1.
    /// Swapping that single entry re-themes every DynamicResource-bound brush in the app.
    /// </summary>
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null)
        {
            return;
        }

        var uri = theme == AppTheme.Dark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var newDictionary = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };

        var dictionaries = app.Resources.MergedDictionaries;
        if (dictionaries.Count > 1)
        {
            dictionaries[1] = newDictionary;
        }
        else
        {
            dictionaries.Add(newDictionary);
        }
    }
}
