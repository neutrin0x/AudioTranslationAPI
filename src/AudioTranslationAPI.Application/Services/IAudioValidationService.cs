using AudioTranslationAPI.Domain.ValueObjects;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Validación específica de archivos de audio
    /// </summary>
    public interface IAudioValidationService
    {
        Task<ValidationResult> ValidateAudioFileAsync(byte[] audioData, string fileName, string contentType, long fileSizeBytes);
        bool IsSupportedFormat(string contentType);
        bool IsFileSizeValid(long fileSizeBytes);
        Task<bool> IsDurationValidAsync(byte[] audioData, string contentType);
    }
}
