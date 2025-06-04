namespace AudioTranslationAPI.Domain.ValueObjects;

public class TranscriptionResult
{
    public string Text { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public TimeSpan Duration { get; set; }
    public string LanguageDetected { get; set; } = string.Empty;
}
