namespace AudioTranslationAPI.Domain.ValueObjects;

public class TranslationResult
{
    public string TranslatedText { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public string DetectedSourceLanguage { get; set; } = string.Empty;
}
