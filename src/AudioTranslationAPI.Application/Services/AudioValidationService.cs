using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AudioTranslationAPI.Application.Services;
public class AudioValidationService : IAudioValidationService
{
    private readonly ILogger<AudioValidationService> _logger;
    private readonly AudioProcessingOptions _options;
    private readonly IAudioProcessingService _audioProcessingService;

    // Formatos soportados con sus content types
    private readonly Dictionary<string, string[]> _supportedFormats = new()
    {
        { "audio/wav", new[] { ".wav", ".wave" } },
        { "audio/mpeg", new[] { ".mp3" } },
        { "audio/mp3", new[] { ".mp3" } },
        { "audio/ogg", new[] { ".ogg" } },
        { "audio/aac", new[] { ".aac" } },
        { "audio/flac", new[] { ".flac" } },
        { "audio/x-wav", new[] { ".wav" } },
        { "audio/x-mpeg", new[] { ".mp3" } }
    };

    public AudioValidationService(
        ILogger<AudioValidationService> logger,
        IOptions<AudioProcessingOptions> options,
        IAudioProcessingService audioProcessingService)
    {
        _logger = logger;
        _options = options.Value;
        _audioProcessingService = audioProcessingService;
    }

    public async Task<ValidationResult> ValidateAudioFileAsync(
        byte[] audioData,
        string fileName,
        string contentType,
        long fileSizeBytes)
    {
        var errors = new List<string>();

        try
        {
            _logger.LogDebug("Validando archivo de audio: {FileName}, Tipo: {ContentType}, Tamaño: {Size} bytes",
                fileName, contentType, fileSizeBytes);

            // 1. Validar que no esté vacío
            if (audioData == null || audioData.Length == 0)
            {
                errors.Add("El archivo de audio está vacío");
                return ValidationResult.Failure(errors);
            }

            // 2. Validar tamaño de archivo
            if (!IsFileSizeValid(fileSizeBytes))
            {
                errors.Add($"El archivo es demasiado grande. Máximo permitido: {_options.MaxFileSizeMB}MB");
            }

            // 3. Validar formato por content type
            if (!IsSupportedFormat(contentType))
            {
                errors.Add($"Formato de audio no soportado: {contentType}. Formatos válidos: {string.Join(", ", _supportedFormats.Keys)}");
            }

            // 4. Validar extensión del archivo
            var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!IsValidFileExtension(fileExtension, contentType))
            {
                errors.Add($"La extensión del archivo ({fileExtension}) no coincide con el tipo de contenido ({contentType})");
            }

            // 5. Validar que sea un archivo de audio real usando FFmpeg
            var isValidAudio = await _audioProcessingService.IsValidAudioAsync(audioData, contentType);
            if (!isValidAudio)
            {
                errors.Add("El archivo no contiene audio válido o está corrupto");
                return ValidationResult.Failure(errors);
            }

            // 6. Obtener y validar metadata del audio
            AudioMetadata? metadata = null;
            try
            {
                metadata = await _audioProcessingService.GetAudioMetadataAsync(audioData, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener metadata del audio");
                errors.Add("No se pudo analizar el archivo de audio. Posiblemente esté corrupto");
                return ValidationResult.Failure(errors);
            }

            // 7. Validar duración del audio
            if (!IsDurationValid(metadata.Duration))
            {
                errors.Add($"La duración del audio ({metadata.Duration:mm\\:ss}) excede el límite de {_options.MaxDurationMinutes} minutos");
            }

            // 8. Validar calidad mínima del audio
            var qualityValidation = ValidateAudioQuality(metadata);
            if (!string.IsNullOrEmpty(qualityValidation))
            {
                errors.Add(qualityValidation);
            }

            // 9. Validar que tenga contenido de audio
            if (metadata.Duration.TotalSeconds < 0.5)
            {
                errors.Add("El archivo de audio es demasiado corto (mínimo 0.5 segundos)");
            }

            // Retornar resultado
            if (errors.Any())
            {
                _logger.LogWarning("Validación falló para {FileName}: {Errors}", fileName, string.Join("; ", errors));
                return ValidationResult.Failure(errors);
            }

            _logger.LogInformation("Archivo de audio válido: {FileName} - Duración: {Duration}, Sample Rate: {SampleRate}Hz",
                fileName, metadata.Duration, metadata.SampleRate);

            return ValidationResult.Success(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado durante la validación del archivo {FileName}", fileName);
            errors.Add($"Error interno durante la validación: {ex.Message}");
            return ValidationResult.Failure(errors);
        }
    }

    public bool IsSupportedFormat(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        return _supportedFormats.ContainsKey(contentType.ToLowerInvariant());
    }

    public bool IsFileSizeValid(long fileSizeBytes)
    {
        var maxSizeBytes = _options.MaxFileSizeMB * 1024 * 1024;
        return fileSizeBytes > 0 && fileSizeBytes <= maxSizeBytes;
    }

    public async Task<bool> IsDurationValidAsync(byte[] audioData, string contentType)
    {
        try
        {
            // Crear un archivo temporal para analizar
            var tempFileName = $"temp_validation_{Guid.NewGuid()}{GetFileExtensionFromContentType(contentType)}";
            var metadata = await _audioProcessingService.GetAudioMetadataAsync(audioData, tempFileName);

            return IsDurationValid(metadata.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al validar duración del audio");
            return false;
        }
    }

    // ===== MÉTODOS AUXILIARES PRIVADOS =====

    private bool IsValidFileExtension(string fileExtension, string contentType)
    {
        if (string.IsNullOrWhiteSpace(fileExtension) || string.IsNullOrWhiteSpace(contentType))
            return false;

        var normalizedContentType = contentType.ToLowerInvariant();

        if (_supportedFormats.TryGetValue(normalizedContentType, out var validExtensions))
        {
            return validExtensions.Contains(fileExtension);
        }

        return false;
    }

    private bool IsDurationValid(TimeSpan duration)
    {
        var maxDuration = TimeSpan.FromMinutes(_options.MaxDurationMinutes);
        return duration > TimeSpan.Zero && duration <= maxDuration;
    }

    private string? ValidateAudioQuality(AudioMetadata metadata)
    {
        var errors = new List<string>();

        // Validar sample rate mínimo
        if (metadata.SampleRate < 8000)
        {
            errors.Add($"Sample rate muy bajo: {metadata.SampleRate}Hz (mínimo 8kHz)");
        }

        // Validar sample rate máximo razonable
        if (metadata.SampleRate > 192000)
        {
            errors.Add($"Sample rate excesivamente alto: {metadata.SampleRate}Hz (máximo 192kHz)");
        }

        // Validar número de canales
        if (metadata.Channels < 1 || metadata.Channels > 8)
        {
            errors.Add($"Número de canales inválido: {metadata.Channels} (entre 1 y 8)");
        }

        // Validar bitrate si está disponible
        if (metadata.BitRate > 0)
        {
            if (metadata.BitRate < 32000) // 32 kbps mínimo
            {
                errors.Add($"Bitrate muy bajo: {metadata.BitRate / 1000}kbps (mínimo 32kbps)");
            }
            else if (metadata.BitRate > 320000) // 320 kbps máximo razonable
            {
                errors.Add($"Bitrate excesivamente alto: {metadata.BitRate / 1000}kbps (máximo 320kbps)");
            }
        }

        return errors.Any() ? string.Join("; ", errors) : null;
    }

    private string GetFileExtensionFromContentType(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "audio/wav" or "audio/wave" or "audio/x-wav" => ".wav",
            "audio/mpeg" or "audio/mp3" or "audio/x-mpeg" => ".mp3",
            "audio/ogg" => ".ogg",
            "audio/aac" => ".aac",
            "audio/flac" => ".flac",
            _ => ".wav"
        };
    }
}