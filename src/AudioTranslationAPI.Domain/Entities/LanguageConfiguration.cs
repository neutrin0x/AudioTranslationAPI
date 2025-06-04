namespace AudioTranslationAPI.Domain.Entities
{
    /// <summary>
    /// Value Object para configuración de idiomas
    /// </summary>
    public class LanguageConfiguration
    {
        public string Code { get; }
        public string Name { get; }
        public string NativeName { get; }
        public bool IsSupported { get; }

        public LanguageConfiguration(string code, string name, string nativeName, bool isSupported = true)
        {
            Code = code;
            Name = name;
            NativeName = nativeName;
            IsSupported = isSupported;
        }

        public static readonly Dictionary<string, LanguageConfiguration> SupportedLanguages = new()
    {
        { "es", new LanguageConfiguration("es", "Spanish", "Español") },
        { "en", new LanguageConfiguration("en", "English", "English") },
        { "fr", new LanguageConfiguration("fr", "French", "Français", false) },
        { "pt", new LanguageConfiguration("pt", "Portuguese", "Português", false) },
        { "it", new LanguageConfiguration("it", "Italian", "Italiano", false) },
        { "de", new LanguageConfiguration("de", "German", "Deutsch", false) }
    };
    }
}
