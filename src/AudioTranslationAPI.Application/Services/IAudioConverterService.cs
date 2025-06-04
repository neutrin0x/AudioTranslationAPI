using AudioTranslationAPI.Domain.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Conversión de formatos de audio
    /// </summary>
    public interface IAudioConverterService
    {
        Task<byte[]> ConvertToWavAsync(byte[] inputAudio, string inputFormat);
        Task<byte[]> ConvertFromWavAsync(byte[] wavAudio, AudioFormat outputFormat);
        Task<AudioFormat> DetectFormatAsync(byte[] audioData, string fileName);
    }
}
