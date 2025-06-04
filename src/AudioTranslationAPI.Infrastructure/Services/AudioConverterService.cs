using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FFMpegCore;

namespace AudioTranslationAPI.Infrastructure.Services;

/// <summary>
/// Servicio especializado para conversión entre formatos de audio usando FFmpeg
/// </summary>
public class AudioConverterService : IAudioConverterService
{
    private readonly ILogger<AudioConverterService> _logger;
    private readonly AudioProcessingOptions _options;

    // Mapeo de formatos a codecs de FFmpeg
    private readonly Dictionary<AudioFormat, string> _codecMapping = new()
    {
        { AudioFormat.Wav, "pcm_s16le" },
        { AudioFormat.Mp3, "mp3" },
        { AudioFormat.Ogg, "libvorbis" },
        { AudioFormat.Aac, "aac" },
        { AudioFormat.Flac, "flac" }
    };

    // Mapeo de extensiones a formatos
    private readonly Dictionary<string, AudioFormat> _extensionMapping = new()
    {
        { ".wav", AudioFormat.Wav },
        { ".wave", AudioFormat.Wav },
        { ".mp3", AudioFormat.Mp3 },
        { ".ogg", AudioFormat.Ogg },
        { ".oga", AudioFormat.Ogg },
        { ".aac", AudioFormat.Aac },
        { ".m4a", AudioFormat.Aac },
        { ".flac", AudioFormat.Flac }
    };

    // Content types a formatos
    private readonly Dictionary<string, AudioFormat> _contentTypeMapping = new()
    {
        { "audio/wav", AudioFormat.Wav },
        { "audio/wave", AudioFormat.Wav },
        { "audio/x-wav", AudioFormat.Wav },
        { "audio/mpeg", AudioFormat.Mp3 },
        { "audio/mp3", AudioFormat.Mp3 },
        { "audio/x-mpeg", AudioFormat.Mp3 },
        { "audio/ogg", AudioFormat.Ogg },
        { "audio/vorbis", AudioFormat.Ogg },
        { "audio/aac", AudioFormat.Aac },
        { "audio/mp4", AudioFormat.Aac },
        { "audio/flac", AudioFormat.Flac },
        { "audio/x-flac", AudioFormat.Flac }
    };

    public AudioConverterService(
        ILogger<AudioConverterService> logger,
        IOptions<AudioProcessingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public async Task<byte[]> ConvertToWavAsync(byte[] inputAudio, string inputFormat)
    {
        try
        {
            var detectedFormat = DetectFormatFromString(inputFormat);

            if (detectedFormat == AudioFormat.Wav)
            {
                _logger.LogDebug("Audio ya está en formato WAV, no se requiere conversión");
                return inputAudio;
            }

            _logger.LogDebug("Convirtiendo audio de {InputFormat} a WAV", detectedFormat);

            return await ConvertAudioInternalAsync(inputAudio, detectedFormat, AudioFormat.Wav, 16000, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al convertir audio a WAV desde formato {InputFormat}", inputFormat);
            throw new InvalidOperationException($"Error en conversión a WAV: {ex.Message}", ex);
        }
    }

    public async Task<byte[]> ConvertFromWavAsync(byte[] wavAudio, AudioFormat outputFormat)
    {
        try
        {
            if (outputFormat == AudioFormat.Wav)
            {
                _logger.LogDebug("El formato de salida ya es WAV, no se requiere conversión");
                return wavAudio;
            }

            _logger.LogDebug("Convirtiendo audio WAV a {OutputFormat}", outputFormat);

            // Usar configuraciones optimizadas para cada formato de salida
            var (sampleRate, channels) = GetOptimalSettingsForFormat(outputFormat);

            return await ConvertAudioInternalAsync(wavAudio, AudioFormat.Wav, outputFormat, sampleRate, channels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al convertir audio WAV a {OutputFormat}", outputFormat);
            throw new InvalidOperationException($"Error en conversión desde WAV: {ex.Message}", ex);
        }
    }

    public async Task<AudioFormat> DetectFormatAsync(byte[] audioData, string fileName)
    {
        try
        {
            // Primero intentar detectar por extensión de archivo
            var extensionFormat = DetectFormatFromFileName(fileName);
            if (extensionFormat != AudioFormat.Wav) // Usar WAV como default
            {
                _logger.LogDebug("Formato detectado por extensión: {Format}", extensionFormat);
                return extensionFormat;
            }

            // Si no se puede por extensión, analizar los primeros bytes (magic numbers)
            var formatFromMagic = DetectFormatFromMagicNumbers(audioData);
            if (formatFromMagic != AudioFormat.Wav)
            {
                _logger.LogDebug("Formato detectado por magic numbers: {Format}", formatFromMagic);
                return formatFromMagic;
            }

            // Como último recurso, usar FFmpeg para detectar el formato
            return await DetectFormatWithFFmpegAsync(audioData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al detectar formato, usando WAV como fallback");
            return AudioFormat.Wav; // Fallback seguro
        }
    }

    // ===== MÉTODOS INTERNOS =====

    private async Task<byte[]> ConvertAudioInternalAsync(
        byte[] inputAudio,
        AudioFormat fromFormat,
        AudioFormat toFormat,
        int sampleRate,
        int channels)
    {
        var inputExtension = GetFileExtension(fromFormat);
        var outputExtension = GetFileExtension(toFormat);

        var inputTempFile = Path.Combine(_options.TempDirectory, $"convert_input_{Guid.NewGuid()}{inputExtension}");
        var outputTempFile = Path.Combine(_options.TempDirectory, $"convert_output_{Guid.NewGuid()}{outputExtension}");

        try
        {
            // Asegurar que el directorio temporal existe
            Directory.CreateDirectory(_options.TempDirectory);

            // Escribir archivo de entrada
            await File.WriteAllBytesAsync(inputTempFile, inputAudio);

            _logger.LogDebug("Convirtiendo: {InputFile} → {OutputFile} (SR: {SampleRate}Hz, Ch: {Channels})",
                Path.GetFileName(inputTempFile), Path.GetFileName(outputTempFile), sampleRate, channels);

            // Configurar argumentos de FFmpeg según el formato de salida
            await FFMpegArguments
                .FromFileInput(inputTempFile)
                .OutputToFile(outputTempFile, true, options =>
                {
                    options
                        .WithAudioSamplingRate(sampleRate)
                        .WithCustomArgument($"-acodec {_codecMapping[toFormat]}")
                        .WithCustomArgument($"-ac {channels}");

                    // Configuraciones específicas por formato
                    switch (toFormat)
                    {
                        case AudioFormat.Mp3:
                            options.WithCustomArgument("-b:a 128k"); // Bitrate para MP3
                            break;

                        case AudioFormat.Ogg:
                            options.WithCustomArgument("-q:a 5"); // Calidad para OGG Vorbis
                            break;

                        case AudioFormat.Aac:
                            options.WithCustomArgument("-b:a 128k");
                            break;

                        case AudioFormat.Flac:
                            options.WithCustomArgument("-compression_level 5");
                            break;
                    }

                    // Filtros de audio para mejorar calidad
                    if (toFormat != AudioFormat.Flac) // FLAC ya es lossless
                    {
                        options.WithCustomArgument("-af highpass=f=80,lowpass=f=8000");
                    }
                })
                .ProcessAsynchronously();

            // Verificar que el archivo de salida se creó
            if (!File.Exists(outputTempFile))
            {
                throw new InvalidOperationException("FFmpeg no generó el archivo de salida");
            }

            var convertedAudio = await File.ReadAllBytesAsync(outputTempFile);

            _logger.LogDebug("Conversión completada: {InputSize} → {OutputSize} bytes",
                inputAudio.Length, convertedAudio.Length);

            return convertedAudio;
        }
        finally
        {
            // Limpiar archivos temporales
            CleanupTempFile(inputTempFile);
            CleanupTempFile(outputTempFile);
        }
    }

    private AudioFormat DetectFormatFromString(string format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return AudioFormat.Wav;

        var normalizedFormat = format.ToLowerInvariant().Trim();

        // Intentar como content type primero
        if (_contentTypeMapping.TryGetValue(normalizedFormat, out var contentTypeFormat))
        {
            return contentTypeFormat;
        }

        // Intentar como extensión
        var extensionFormat = normalizedFormat.StartsWith(".") ? normalizedFormat : $".{normalizedFormat}";
        if (_extensionMapping.TryGetValue(extensionFormat, out var extFormat))
        {
            return extFormat;
        }

        // Intentar match directo con enum
        if (Enum.TryParse<AudioFormat>(normalizedFormat, true, out var enumFormat))
        {
            return enumFormat;
        }

        return AudioFormat.Wav; // Default fallback
    }

    private AudioFormat DetectFormatFromFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return AudioFormat.Wav;

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (_extensionMapping.TryGetValue(extension, out var format))
        {
            return format;
        }

        return AudioFormat.Wav;
    }

    private AudioFormat DetectFormatFromMagicNumbers(byte[] audioData)
    {
        if (audioData == null || audioData.Length < 12)
            return AudioFormat.Wav;

        // WAV: "RIFF" en los primeros 4 bytes, "WAVE" en bytes 8-11
        if (audioData.Length >= 12 &&
            audioData[0] == 0x52 && audioData[1] == 0x49 && audioData[2] == 0x46 && audioData[3] == 0x46 && // "RIFF"
            audioData[8] == 0x57 && audioData[9] == 0x41 && audioData[10] == 0x56 && audioData[11] == 0x45) // "WAVE"
        {
            return AudioFormat.Wav;
        }

        // MP3: Buscar frame header MP3
        if (audioData.Length >= 3 &&
            ((audioData[0] == 0xFF && (audioData[1] & 0xE0) == 0xE0) || // Frame sync
             (audioData[0] == 0x49 && audioData[1] == 0x44 && audioData[2] == 0x33))) // ID3 tag
        {
            return AudioFormat.Mp3;
        }

        // OGG: "OggS"
        if (audioData.Length >= 4 &&
            audioData[0] == 0x4F && audioData[1] == 0x67 && audioData[2] == 0x67 && audioData[3] == 0x53)
        {
            return AudioFormat.Ogg;
        }

        // FLAC: "fLaC"
        if (audioData.Length >= 4 &&
            audioData[0] == 0x66 && audioData[1] == 0x4C && audioData[2] == 0x61 && audioData[3] == 0x43)
        {
            return AudioFormat.Flac;
        }

        return AudioFormat.Wav; // Default fallback
    }

    private async Task<AudioFormat> DetectFormatWithFFmpegAsync(byte[] audioData)
    {
        var tempFile = Path.Combine(_options.TempDirectory, $"detect_{Guid.NewGuid()}.tmp");

        try
        {
            Directory.CreateDirectory(_options.TempDirectory);
            await File.WriteAllBytesAsync(tempFile, audioData);

            var mediaInfo = await FFProbe.AnalyseAsync(tempFile);
            var audioStream = mediaInfo.PrimaryAudioStream;

            if (audioStream != null)
            {
                return audioStream.CodecName?.ToLowerInvariant() switch
                {
                    "pcm_s16le" or "pcm_f32le" => AudioFormat.Wav,
                    "mp3" => AudioFormat.Mp3,
                    "vorbis" => AudioFormat.Ogg,
                    "aac" => AudioFormat.Aac,
                    "flac" => AudioFormat.Flac,
                    _ => AudioFormat.Wav
                };
            }

            return AudioFormat.Wav;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error al detectar formato con FFmpeg");
            return AudioFormat.Wav;
        }
        finally
        {
            CleanupTempFile(tempFile);
        }
    }

    private (int sampleRate, int channels) GetOptimalSettingsForFormat(AudioFormat format)
    {
        return format switch
        {
            AudioFormat.Mp3 => (22050, 1),    // Mono, calidad estándar
            AudioFormat.Ogg => (22050, 1),    // Mono, calidad estándar
            AudioFormat.Aac => (22050, 1),    // Mono, calidad estándar
            AudioFormat.Flac => (44100, 2),   // Stereo, alta calidad
            AudioFormat.Wav => (16000, 1),    // Mono, optimizado para STT
            _ => (22050, 1)                   // Default
        };
    }

    private string GetFileExtension(AudioFormat format)
    {
        return format switch
        {
            AudioFormat.Wav => ".wav",
            AudioFormat.Mp3 => ".mp3",
            AudioFormat.Ogg => ".ogg",
            AudioFormat.Aac => ".aac",
            AudioFormat.Flac => ".flac",
            _ => ".wav"
        };
    }

    private void CleanupTempFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo eliminar archivo temporal: {FilePath}", filePath);
        }
    }
}