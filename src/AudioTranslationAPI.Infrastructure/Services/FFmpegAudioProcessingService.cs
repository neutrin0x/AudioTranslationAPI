using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.ValueObjects;
using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Domain.Types;
using FFMpegCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace AudioTranslationAPI.Infrastructure.Services;

public class FFmpegAudioProcessingService : IAudioProcessingService
{
    private readonly ILogger<FFmpegAudioProcessingService> _logger;
    private readonly AudioProcessingOptions _options;

    public FFmpegAudioProcessingService(
        ILogger<FFmpegAudioProcessingService> logger,
        IOptions<AudioProcessingOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        // Configurar FFmpeg path si está especificado
        if (!string.IsNullOrEmpty(_options.FFmpegPath))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = Path.GetDirectoryName(_options.FFmpegPath) });
        }
    }

    public async Task<bool> ValidateFFmpegInstallationAsync()
    {
        try
        {
            _logger.LogInformation("Validando instalación de FFmpeg...");

            // Verificar que FFmpeg esté disponible
            var processInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                _logger.LogError("No se pudo iniciar el proceso FFmpeg");
                return false;
            }

            await process.WaitForExitAsync();
            var output = await process.StandardOutput.ReadToEndAsync();

            if (process.ExitCode == 0 && output.Contains("ffmpeg version"))
            {
                _logger.LogInformation("FFmpeg está disponible: {Version}",
                    output.Split('\n')[0]);
                return true;
            }

            _logger.LogError("FFmpeg no está disponible o no funciona correctamente");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar FFmpeg");
            return false;
        }
    }

    public async Task<AudioMetadata> GetAudioMetadataAsync(byte[] audioData, string fileName)
    {
        var tempFilePath = Path.Combine(_options.TempDirectory, $"temp_{Guid.NewGuid()}{Path.GetExtension(fileName)}");

        try
        {
            // Asegurar que el directorio temporal existe
            Directory.CreateDirectory(_options.TempDirectory);

            // Escribir archivo temporal
            await File.WriteAllBytesAsync(tempFilePath, audioData);

            _logger.LogDebug("Analizando metadata de audio: {FileName}", fileName);

            // Analizar con FFmpeg
            var mediaInfo = await FFProbe.AnalyseAsync(tempFilePath);

            var audioStream = mediaInfo.PrimaryAudioStream;
            if (audioStream == null)
            {
                throw new InvalidOperationException("No se encontró stream de audio en el archivo");
            }

            var metadata = new AudioMetadata
            {
                Duration = mediaInfo.Duration,
                SampleRate = audioStream.SampleRateHz,
                Channels = audioStream.Channels,
                BitRate = (int)(audioStream.BitRate > 0 ? audioStream.BitRate : 0),
                Format = DetectAudioFormat(Path.GetExtension(fileName)),
                FileSizeBytes = audioData.Length
            };

            _logger.LogDebug("Metadata extraída: Duración={Duration}, SampleRate={SampleRate}, Channels={Channels}",
                metadata.Duration, metadata.SampleRate, metadata.Channels);

            return metadata;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener metadata del audio");
            throw new InvalidOperationException($"Error al analizar archivo de audio: {ex.Message}", ex);
        }
        finally
        {
            // Limpiar archivo temporal
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "No se pudo eliminar archivo temporal: {TempFile}", tempFilePath);
                }
            }
        }
    }

    public async Task<byte[]> ConvertAudioAsync(byte[] inputAudio, AudioFormat fromFormat, AudioFormat toFormat, int sampleRate = 16000)
    {
        if (fromFormat == toFormat)
        {
            _logger.LogDebug("Formato de entrada y salida son iguales, retornando audio original");
            return inputAudio;
        }

        var inputTempFile = Path.Combine(_options.TempDirectory, $"input_{Guid.NewGuid()}.{GetFileExtension(fromFormat)}");
        var outputTempFile = Path.Combine(_options.TempDirectory, $"output_{Guid.NewGuid()}.{GetFileExtension(toFormat)}");

        try
        {
            Directory.CreateDirectory(_options.TempDirectory);

            // Escribir archivo de entrada
            await File.WriteAllBytesAsync(inputTempFile, inputAudio);

            _logger.LogDebug("Convirtiendo audio de {FromFormat} a {ToFormat} con sample rate {SampleRate}",
                fromFormat, toFormat, sampleRate);

            // Realizar conversión con FFmpeg
            await FFMpegArguments
                .FromFileInput(inputTempFile)
                .OutputToFile(outputTempFile, true, options => options
                    .WithAudioSamplingRate(sampleRate)
                    .WithAudioBitrate(_options.DefaultBitRate)
                    .WithCustomArgument($"-acodec {GetAudioCodecString(toFormat)}")
                    .WithCustomArgument("-ac 1") // Mono channel para STT
                )
                .ProcessAsynchronously();

            if (!File.Exists(outputTempFile))
            {
                throw new InvalidOperationException("La conversión de audio falló - archivo de salida no generado");
            }

            var convertedAudio = await File.ReadAllBytesAsync(outputTempFile);

            _logger.LogDebug("Conversión completada. Tamaño original: {OriginalSize} bytes, Convertido: {ConvertedSize} bytes",
                inputAudio.Length, convertedAudio.Length);

            return convertedAudio;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la conversión de audio");
            throw new InvalidOperationException($"Error al convertir audio: {ex.Message}", ex);
        }
        finally
        {
            // Limpiar archivos temporales
            CleanupTempFile(inputTempFile);
            CleanupTempFile(outputTempFile);
        }
    }

    public async Task<byte[]> NormalizeAudioAsync(byte[] audioData, AudioFormat format)
    {
        var inputTempFile = Path.Combine(_options.TempDirectory, $"normalize_input_{Guid.NewGuid()}.{GetFileExtension(format)}");
        var outputTempFile = Path.Combine(_options.TempDirectory, $"normalize_output_{Guid.NewGuid()}.{GetFileExtension(format)}");

        try
        {
            Directory.CreateDirectory(_options.TempDirectory);

            await File.WriteAllBytesAsync(inputTempFile, audioData);

            _logger.LogDebug("Normalizando audio de {Size} bytes", audioData.Length);

            // Normalizar volumen y aplicar filtros de limpieza
            await FFMpegArguments
                .FromFileInput(inputTempFile)
                .OutputToFile(outputTempFile, true, options => options
                    .WithAudioSamplingRate(_options.DefaultSampleRate)
                    .WithCustomArgument("-af") // Audio filters
                    .WithCustomArgument("loudnorm,highpass=f=80,lowpass=f=8000") // Normalización y filtros de ruido
                    .WithCustomArgument($"-acodec {GetAudioCodecString(format)}")
                )
                .ProcessAsynchronously();

            if (!File.Exists(outputTempFile))
            {
                throw new InvalidOperationException("La normalización de audio falló");
            }

            var normalizedAudio = await File.ReadAllBytesAsync(outputTempFile);

            _logger.LogDebug("Normalización completada. Audio normalizado: {Size} bytes", normalizedAudio.Length);

            return normalizedAudio;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la normalización de audio");
            throw new InvalidOperationException($"Error al normalizar audio: {ex.Message}", ex);
        }
        finally
        {
            CleanupTempFile(inputTempFile);
            CleanupTempFile(outputTempFile);
        }
    }

    public async Task<bool> IsValidAudioAsync(byte[] audioData, string contentType)
    {
        try
        {
            var tempFile = Path.Combine(_options.TempDirectory, $"validate_{Guid.NewGuid()}.tmp");

            Directory.CreateDirectory(_options.TempDirectory);
            await File.WriteAllBytesAsync(tempFile, audioData);

            // Intentar analizar el archivo con FFProbe
            var mediaInfo = await FFProbe.AnalyseAsync(tempFile);

            CleanupTempFile(tempFile);

            // Verificar que tiene stream de audio
            return mediaInfo.PrimaryAudioStream != null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Archivo no es un audio válido");
            return false;
        }
    }

    // ===== MÉTODOS AUXILIARES =====

    private AudioFormat DetectAudioFormat(string fileExtension)
    {
        return fileExtension.ToLowerInvariant() switch
        {
            ".wav" => AudioFormat.Wav,
            ".mp3" => AudioFormat.Mp3,
            ".ogg" => AudioFormat.Ogg,
            ".aac" => AudioFormat.Aac,
            ".flac" => AudioFormat.Flac,
            _ => AudioFormat.Wav // Default
        };
    }

    private string GetFileExtension(AudioFormat format)
    {
        return format switch
        {
            AudioFormat.Wav => "wav",
            AudioFormat.Mp3 => "mp3",
            AudioFormat.Ogg => "ogg",
            AudioFormat.Aac => "aac",
            AudioFormat.Flac => "flac",
            _ => "wav"
        };
    }

    private string GetAudioCodecString(AudioFormat format)
    {
        return format switch
        {
            AudioFormat.Wav => "pcm_s16le",
            AudioFormat.Mp3 => "mp3",
            AudioFormat.Ogg => "libvorbis",
            AudioFormat.Aac => "aac",
            AudioFormat.Flac => "flac",
            _ => "pcm_s16le"
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