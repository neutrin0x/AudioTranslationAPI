using AudioTranslationAPI.Application.DTOs;
using AudioTranslationAPI.Domain.Entities;
using AudioTranslationAPI.Domain.Types;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace AudioTranslationAPI.Application.Services;

public class AudioTranslationService : IAudioTranslationService
{
    private readonly ILogger<AudioTranslationService> _logger;
    private readonly IAudioRepository _audioRepository;
    private readonly IAudioProcessingService _audioProcessingService;
    private readonly IAudioValidationService _audioValidationService;
    private readonly IFileStorageService _fileStorageService;
    private readonly IAudioTranslationBackgroundService _backgroundService;
    private readonly IBackgroundJobClient _backgroundJobClient;

    public AudioTranslationService(
        ILogger<AudioTranslationService> logger,
        IAudioRepository audioRepository,
        IAudioProcessingService audioProcessingService,
        IAudioValidationService audioValidationService,
        IFileStorageService fileStorageService,
        IAudioTranslationBackgroundService backgroundService,
        IBackgroundJobClient backgroundJobClient)
    {
        _logger = logger;
        _audioRepository = audioRepository;
        _audioProcessingService = audioProcessingService;
        _audioValidationService = audioValidationService;
        _fileStorageService = fileStorageService;
        _backgroundService = backgroundService;
        _backgroundJobClient = backgroundJobClient;
    }

    public async Task<TranslationResponseDto> StartTranslationAsync(TranslationRequestDto request)
    {
        try
        {
            _logger.LogInformation("Iniciando nueva traducción: {SourceLang} -> {TargetLang}, Archivo: {FileName}",
                request.SourceLanguage, request.TargetLanguage, request.FileName);

            // 1. Validar archivo de audio
            //var validationResult = await _audioValidationService.ValidateAudioFileAsync(
            //    request.AudioData,
            //    request.FileName,
            //    request.ContentType,
            //    request.FileSizeBytes);

            //if (!validationResult.IsValid)
            //{
            //    var errorMessage = string.Join(", ", validationResult.Errors);
            //    _logger.LogWarning("Validación de audio falló: {Errors}", errorMessage);
            //    throw new ArgumentException($"Archivo de audio inválido: {errorMessage}");
            //}

            // 2. Obtener metadata del audio
            var metadata = await _audioProcessingService.GetAudioMetadataAsync(request.AudioData, request.FileName);

            _logger.LogDebug("Metadata del audio: Duración={Duration}, SampleRate={SampleRate}, Canales={Channels}",
                metadata.Duration, metadata.SampleRate, metadata.Channels);

            // 3. Crear entidad de traducción
            var audioFormat = DetectAudioFormat(request.ContentType);
            var translation = AudioTranslation.Create(
                sourceLanguage: request.SourceLanguage,
                targetLanguage: request.TargetLanguage,
                originalFileName: request.FileName,
                originalFileSizeBytes: request.FileSizeBytes,
                originalDuration: metadata.Duration,
                inputFormat: audioFormat,
                userId: request.UserId
            );

            // 4. Guardar archivo original
            var originalFilePath = await _fileStorageService.SaveAudioAsync(
                request.AudioData,
                $"{translation.Id}_original{Path.GetExtension(request.FileName)}",
                "originals");

            translation.SetFilePaths(originalPath: originalFilePath);
            translation.UpdateStatus(TranslationStatus.Queued, "Archivo guardado, en cola para procesamiento", 10);

            // 5. Guardar en repositorio
            await _audioRepository.CreateAsync(translation);

            Console.WriteLine($"=== TRANSLATION GUARDADA CON ID: {translation.Id} ===");


            // 6. Encolar trabajo en background con Hangfire
            var jobId = _backgroundJobClient.Enqueue(() =>
                _backgroundService.ProcessTranslationAsync(translation.Id));

            _logger.LogInformation("Traducción {TranslationId} encolada con job {JobId}", translation.Id, jobId);

            // 7. Estimar tiempo de completado
            var estimatedTime = EstimateCompletionTime(metadata.Duration);

            return new TranslationResponseDto
            {
                TranslationId = translation.Id,
                Status = translation.Status,
                Message = "Traducción iniciada correctamente",
                CreatedAt = translation.CreatedAt,
                EstimatedCompletionTime = estimatedTime
            };
        }
        catch (ArgumentException)
        {
            throw; // Re-throw validation errors
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al iniciar traducción");
            throw new InvalidOperationException("Error interno al procesar la solicitud", ex);
        }
    }

    public async Task<TranslationStatusDto?> GetTranslationStatusAsync(Guid translationId)
    {
        try
        {
            Console.WriteLine($"=== BUSCANDO TRANSLATION ID: {translationId} ===");


            var translation = await _audioRepository.GetByIdAsync(translationId);

            if (translation == null)
            {
                _logger.LogWarning("Traducción no encontrada: {TranslationId}", translationId);
                return null;
            }

            // Verificar si ha expirado
            if (translation.IsExpired() && translation.Status != TranslationStatus.Completed)
            {
                translation.UpdateStatus(TranslationStatus.Expired, "Traducción expirada");
                await _audioRepository.UpdateAsync(translation);
            }

            var estimatedTimeRemaining = translation.Status == TranslationStatus.Completed
                ? null
                : EstimateRemainingTime(translation);

            return new TranslationStatusDto
            {
                TranslationId = translation.Id,
                Status = translation.Status,
                Progress = translation.Progress,
                CurrentStep = translation.CurrentStep,
                ErrorMessage = translation.ErrorMessage,
                CreatedAt = translation.CreatedAt,
                CompletedAt = translation.CompletedAt,
                EstimatedTimeRemaining = estimatedTimeRemaining
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener estado de traducción {TranslationId}", translationId);
            throw;
        }
    }

    public async Task<TranslatedAudioResultDto?> GetTranslatedAudioAsync(Guid translationId)
    {
        try
        {
            var translation = await _audioRepository.GetByIdAsync(translationId);

            if (translation == null)
            {
                _logger.LogWarning("Traducción no encontrada: {TranslationId}", translationId);
                return null;
            }

            var result = new TranslatedAudioResultDto
            {
                TranslationId = translation.Id,
                IsCompleted = translation.Status == TranslationStatus.Completed,
                Status = translation.Status,
                Progress = translation.Progress
            };

            // Si no está completada, retornar solo el estado
            if (!result.IsCompleted)
            {
                return result;
            }

            // Si está completada, cargar el archivo de audio
            if (!string.IsNullOrEmpty(translation.TranslatedAudioPath))
            {
                try
                {
                    var audioData = await _fileStorageService.LoadAudioAsync(translation.TranslatedAudioPath);
                    var outputExtension = Path.GetExtension(translation.TranslatedAudioPath);

                    result.AudioData = audioData;
                    result.ContentType = GetContentType(outputExtension);
                    result.FileName = $"{translation.Id}_translated{outputExtension}";
                    result.FileSizeBytes = audioData.Length;

                    _logger.LogDebug("Audio traducido cargado: {Size} bytes", audioData.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al cargar audio traducido {TranslationId}", translationId);
                    throw new InvalidOperationException("Error al cargar archivo de audio traducido");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener audio traducido {TranslationId}", translationId);
            throw;
        }
    }

    public async Task<bool> CancelTranslationAsync(Guid translationId)
    {
        try
        {
            var translation = await _audioRepository.GetByIdAsync(translationId);

            if (translation == null)
            {
                return false;
            }

            if (translation.Status == TranslationStatus.Completed ||
                translation.Status == TranslationStatus.Failed ||
                translation.Status == TranslationStatus.Cancelled)
            {
                return false; // No se puede cancelar
            }

            translation.Cancel();
            await _audioRepository.UpdateAsync(translation);

            _logger.LogInformation("Traducción cancelada: {TranslationId}", translationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cancelar traducción {TranslationId}", translationId);
            return false;
        }
    }

    // ===== MÉTODOS AUXILIARES =====

    private AudioFormat DetectAudioFormat(string contentType)
    {
        return contentType.ToLowerInvariant() switch
        {
            "audio/wav" or "audio/wave" => AudioFormat.Wav,
            "audio/mpeg" or "audio/mp3" => AudioFormat.Mp3,
            "audio/ogg" => AudioFormat.Ogg,
            "audio/aac" => AudioFormat.Aac,
            "audio/flac" => AudioFormat.Flac,
            _ => AudioFormat.Wav
        };
    }

    private string GetContentType(string fileExtension)
    {
        return fileExtension.ToLowerInvariant() switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".aac" => "audio/aac",
            ".flac" => "audio/flac",
            _ => "audio/wav"
        };
    }

    private string EstimateCompletionTime(TimeSpan audioDuration)
    {
        // Estimación basada en duración del audio
        // Típicamente: STT (1x), Translation (0.1x), TTS (2x) = ~3x la duración del audio
        var estimatedSeconds = (int)(audioDuration.TotalSeconds * 3) + 30; // +30s overhead

        return estimatedSeconds switch
        {
            < 60 => $"{estimatedSeconds} segundos",
            < 3600 => $"{estimatedSeconds / 60} minutos",
            _ => $"{estimatedSeconds / 3600} horas"
        };
    }

    private string? EstimateRemainingTime(AudioTranslation translation)
    {
        if (translation.StartedAt == null) return "Calculando...";

        var elapsed = DateTime.UtcNow - translation.StartedAt.Value;
        var progressPercent = Math.Max(translation.Progress, 1); // Evitar división por 0
        var estimatedTotal = elapsed.TotalSeconds * (100.0 / progressPercent);
        var remaining = Math.Max(0, estimatedTotal - elapsed.TotalSeconds);

        return remaining switch
        {
            < 60 => $"{(int)remaining} segundos",
            < 3600 => $"{(int)(remaining / 60)} minutos",
            _ => $"{(int)(remaining / 3600)} horas"
        };
    }
}