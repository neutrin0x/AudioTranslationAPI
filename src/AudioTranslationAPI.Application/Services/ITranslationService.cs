using AudioTranslationAPI.Domain.ValueObjects;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Traducción de texto
    /// </summary>
    public interface ITranslationService
    {
        Task<string> TranslateAsync(string text, string fromLanguage, string toLanguage);
        Task<TranslationResult> TranslateWithConfidenceAsync(string text, string fromLanguage, string toLanguage);
        Task<bool> IsLanguagePairSupportedAsync(string fromLanguage, string toLanguage);
    }
}
