using AudioTranslationAPI.Domain.ValueObjects;
using AudioTranslationAPI.Domain.Types;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Conversión de texto a audio
    /// </summary>
    public interface ITextToSpeechService
    {
        Task<byte[]> SynthesizeAsync(string text, string languageCode, string? voiceId = null);
        Task<AudioSynthesisResult> SynthesizeWithOptionsAsync(string text, string languageCode, AudioQuality quality, string? voiceId = null);
        Task<IEnumerable<VoiceInfo>> GetAvailableVoicesAsync(string languageCode);
    }
}
