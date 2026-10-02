using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace SOR.Presentation.Services;

/// <summary>Motyw kolorystyczny aplikacji.</summary>
public enum AppTheme
{
    /// <summary>Motyw jasny — domyślny.</summary>
    Light = 0,

    /// <summary>Motyw ciemny — przeznaczony do pracy nocnej na dyżurze.</summary>
    Dark = 1
}

/// <summary>Serwis motywu — podmiana słownika zasobów i zapamiętanie wyboru użytkownika.</summary>
public interface IThemeService
{
    /// <summary>Aktywny motyw.</summary>
    AppTheme Current { get; }

    /// <summary>Zdarzenie zmiany motywu (dla elementów, które nie korzystają z DynamicResource).</summary>
    event EventHandler<AppTheme>? ThemeChanged;

    /// <summary>Przełącza motyw na przeciwny.</summary>
    void Toggle();

    /// <summary>Wczytuje zapisany motyw i stosuje go do zasobów aplikacji.</summary>
    void Initialize();

    /// <summary>Ustawia wskazany motyw.</summary>
    void Apply(AppTheme theme);
}

/// <summary>
/// Implementacja przełącznika motywów.
///
/// Motyw jest realizowany jako podmiana słownika <c>ResourceDictionary</c> w zasobach aplikacji.
/// Wszystkie kolory w widokach są wiązane przez <c>DynamicResource</c>, więc zmiana słownika
/// przemalowuje interfejs bez rekompilacji widoków. Wybór jest zapisywany w pliku obok bazy
/// danych, aby obowiązywał również po ponownym uruchomieniu aplikacji.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string DarkSource = "/SOR.Presentation;component/Themes/Dark.xaml";
    private const string LightSource = "/SOR.Presentation;component/Themes/Light.xaml";

    private readonly ILogger<ThemeService> _logger;
    private readonly string _settingsPath;
    private AppTheme _current = AppTheme.Light;

    public ThemeService(ILogger<ThemeService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SOR");

        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "ui-theme.json");
    }

    public AppTheme Current => _current;

    public event EventHandler<AppTheme>? ThemeChanged;

    public void Initialize() => Apply(LoadPersistedTheme());

    public void Toggle() => Apply(_current == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);

    public void Apply(AppTheme theme)
    {
        var resources = System.Windows.Application.Current?.Resources;
        if (resources is null)
        {
            return;
        }

        var source = theme == AppTheme.Dark ? DarkSource : LightSource;

        try
        {
            var dictionary = new ResourceDictionary { Source = new Uri(source, UriKind.Relative) };

            // Usunięcie poprzedniego słownika motywu, aby nie kumulować zasobów przy przełączaniu.
            for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                if (IsThemeDictionary(resources.MergedDictionaries[i]))
                {
                    resources.MergedDictionaries.RemoveAt(i);
                }
            }

            resources.MergedDictionaries.Add(dictionary);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Nie udało się zastosować motywu {Theme}.", theme);
            return;
        }

        _current = theme;
        Persist(theme);
        ThemeChanged?.Invoke(this, theme);
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString is string source &&
        (source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
         source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase));

    private AppTheme LoadPersistedTheme()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppTheme.Light;
            }

            var json = File.ReadAllText(_settingsPath).Trim();

            if (json.Length == 0)
            {
                return AppTheme.Light;
            }

            // Plik zawiera nazwę motywu w cudzysłowie JSON ("Dark"), ale przyjęliśmy też
            // zapis samej nazwy — wartość bez cudzysłowów przechodzi konwersję bez zmian.
            var name = json.Length > 1 && json[0] == '"' && json[^1] == '"'
                ? json[1..^1]
                : json;

            return Enum.TryParse<AppTheme>(name, ignoreCase: true, out var theme)
                ? theme
                : AppTheme.Light;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Nie udało się odczytać zapisanego motywu — przywracam jasny.");
            return AppTheme.Light;
        }
    }

    private void Persist(AppTheme theme)
    {
        try
        {
            // Zapis nazwanej wartości (np. "Dark"), a nie liczby — plik ma pozostać czytelny,
            // a odczyt ponownie konwertuje nazwę z powrotem na wyliczenie.
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(theme.ToString()));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Nie udało się zapisać wyboru motywu.");
        }
    }
}

