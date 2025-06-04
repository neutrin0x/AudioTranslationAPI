using AudioTranslationAPI.Domain.ValueObjects;
using AudioTranslationAPI.Domain.Types;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Servicio principal para procesamiento de audio usando FFmpeg
    /// </summary>
    public interface IAudioProcessingService
    {
        Task<bool> ValidateFFmpegInstallationAsync();
        Task<AudioMetadata> GetAudioMetadataAsync(byte[] audioData, string fileName);
        Task<byte[]> ConvertAudioAsync(byte[] inputAudio, AudioFormat fromFormat, AudioFormat toFormat, int sampleRate = 16000);
        Task<byte[]> NormalizeAudioAsync(byte[] audioData, AudioFormat format);
        Task<bool> IsValidAudioAsync(byte[] audioData, string contentType);
    }
}
