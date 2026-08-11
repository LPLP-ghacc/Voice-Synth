using System.Windows;

namespace VoiceSynthWPF;

public static class LocalizationManager
{
    private const string DictUriTemplate =
        "pack://application:,,,/VoiceSynthWPF;component/Resources/Strings.{0}.xaml";

    public static string CurrentLanguage { get; private set; } = "en";

    public static readonly string[] SupportedLanguages = ["en", "ru"];

    /// <summary>Переключает язык интерфейса без перезапуска приложения.</summary>
    public static void SetLanguage(string lang)
    {
        if (!SupportedLanguages.Contains(lang))
            lang = "en";

        CurrentLanguage = lang;

        var uri  = new Uri(string.Format(DictUriTemplate, lang));
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;

        // Удаляем старый словарь строк
        var old = merged.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("/Resources/Strings.") == true);
        if (old != null)
            merged.Remove(old);

        merged.Add(dict);
    }

    /// <summary>Инициализация: берём язык системы, фоллбэк на английский.</summary>
    public static void InitFromSystem()
    {
        var culture = System.Globalization.CultureInfo.CurrentUICulture;
        var lang    = culture.TwoLetterISOLanguageName; // "ru", "en", ...
        SetLanguage(SupportedLanguages.Contains(lang) ? lang : "en");
    }

    /// <summary>Получить строку по ключу (для code-behind).</summary>
    public static string Get(string key) =>
        Application.Current.TryFindResource(key) as string ?? key;
}
