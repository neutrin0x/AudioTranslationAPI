namespace AudioTranslationAPI.Domain.ValueObjects;

public class VoiceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
}
