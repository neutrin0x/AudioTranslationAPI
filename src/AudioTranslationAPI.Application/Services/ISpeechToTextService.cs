using AudioTranslationAPI.Domain.ValueObjects;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Conversión de audio a texto
    /// </summary>
    public interface ISpeechToTextService
    {
        Task<string> TranscribeAsync(byte[] audioData, string languageCode);
        Task<TranscriptionResult> TranscribeWithConfidenceAsync(byte[] audioData, string languageCode);
        bool IsLanguageSupported(string languageCode);
    }
}
