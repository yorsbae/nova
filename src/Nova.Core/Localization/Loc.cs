namespace Nova.Core.Localization;

/// <summary>
/// Text for the Settings window in two languages. The source strings are Indonesian; <see cref="T"/> returns the English
/// version when the language is "en" and the Indonesian text itself when no translation exists (so nothing ever shows blank).
/// </summary>
public static class Loc
{
    public static string Current { get; private set; } = "id";

    public static void Set(string? language) => Current = language == "en" ? "en" : "id";

    public static string T(string indonesian) =>
        Current == "en" && SettingsEnglish.Map.TryGetValue(indonesian, out var en) ? en : indonesian;

    /// <summary>Translates the template, then fills {0}, {1}… (for messages with a name or a number in them).</summary>
    public static string F(string indonesianTemplate, params object[] args) => string.Format(T(indonesianTemplate), args);
}
