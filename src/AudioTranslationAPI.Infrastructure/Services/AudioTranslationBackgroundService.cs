using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Domain.Entities;
using AudioTranslationAPI.Domain.Types;
using AudioTranslationAPI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AudioTranslationAPI.Infrastructure.Services;

/// <summary>
/// Servicio de background que maneja el procesamiento completo de traducción de audio
/// Ejecuta el pipeline: Audio → Speech-to-Text → Translation → Text-to-Speech → Audio
/// </summary>
public class AudioTranslationBackgroundService : IAudioTranslationBackgroundService
{
    private readonly ILogger<AudioTranslationBackgroundService> _logger;
    private readonly IAudioRepository _audioRepository;
    private readonly IAudioProcessingService _audioProcessingService;
    private readonly IAudioConverterService _audioConverterService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ISpeechToTextService _speechToTextService;
    private readonly ITranslationService _translationService;
    private readonly ITextToSpeechService _textToSpeechService;

    public AudioTranslationBackgroundService(
        ILogger<AudioTranslationBackgroundService> logger,
        IAudioRepository audioRepository,
        IAudioProcessingService audioProcessingService,
        IAudioConverterService audioConverterService,
        IFileStorageService fileStorageService,
        ISpeechToTextService speechToTextService,
        ITranslationService translationService,
        ITextToSpeechService textToSpeechService)
    {
        _logger = logger;
        _audioRepository = audioRepository;
        _audioProcessingService = audioProcessingService;
        _audioConverterService = audioConverterService;
        _fileStorageService = fileStorageService;
        _speechToTextService = speechToTextService;
        _translationService = translationService;
        _textToSpeechService = textToSpeechService;
    }

    public async Task ProcessTranslationAsync(Guid translationId)
    {
        var stopwatch = Stopwatch.StartNew();
        AudioTranslation? translation = null;

        try
        {
            _logger.LogInformation("Iniciando procesamiento de traducción: {TranslationId}", translationId);

            // 1. Obtener la traducción
            translation = await _audioRepository.GetByIdAsync(translationId);
            if (translation == null)
            {
                _logger.LogError("Traducción no encontrada: {TranslationId}", translationId);
                return;
            }

            // Verificar que esté en estado válido para procesar
            if (translation.Status != TranslationStatus.Queued)
            {
                _logger.LogWarning("Traducción {TranslationId} no está en estado Queued. Estado actual: {Status}",
                    translationId, translation.Status);
                return;
            }

            // 2. Verificar si ha expirado
            if (translation.IsExpired())
            {
                translation.UpdateStatus(TranslationStatus.Expired, "Traducción expirada antes del procesamiento");
                await _audioRepository.UpdateAsync(translation);
                _logger.LogWarning("Traducción {TranslationId} expiró antes del procesamiento", translationId);
                return;
            }

            // 3. Marcar como iniciada
            translation.UpdateStatus(TranslationStatus.Validating, "Validando archivo de audio", 15);
            await _audioRepository.UpdateAsync(translation);

            // 4. Procesar paso a paso
            await Step1_PrepareAudioAsync(translation);
            await Step2_SpeechToTextAsync(translation);
            await Step3_TranslateTextAsync(translation);
            await Step4_TextToSpeechAsync(translation);
            await Step5_FinalizeAsync(translation);

            stopwatch.Stop();
            _logger.LogInformation("Traducción {TranslationId} completada exitosamente en {Duration:mm\\:ss}",
                translationId, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error durante el procesamiento de traducción {TranslationId} después de {Duration:mm\\:ss}",
                translationId, stopwatch.Elapsed);

            if (translation != null)
            {
                translation.SetError($"Error durante el procesamiento: {ex.Message}", ex.ToString());
                translation.IncrementRetry();
                await _audioRepository.UpdateAsync(translation);

                // Si puede reintentarse y no ha alcanzado el límite, programar reintento
                if (translation.CanBeRetried())
                {
                    _logger.LogInformation("Programando reintento para traducción {TranslationId} (intento {RetryCount})",
                        translationId, translation.RetryCount);

                    // TODO: Programar reintento con Hangfire con delay
                    // BackgroundJob.Schedule(() => RetryFailedTranslationAsync(translationId), TimeSpan.FromMinutes(5));
                }
            }

            throw; // Re-lanzar para que Hangfire lo marque como fallido
        }
    }

    public async Task RetryFailedTranslationAsync(Guid translationId)
    {
        try
        {
            _logger.LogInformation("Reintentando traducción fallida: {TranslationId}", translationId);

            var translation = await _audioRepository.GetByIdAsync(translationId);
            if (translation == null)
            {
                _logger.LogError("Traducción no encontrada para reintento: {TranslationId}", translationId);
                return;
            }

            if (!translation.CanBeRetried())
            {
                _logger.LogWarning("Traducción {TranslationId} no puede ser reintentada. Estado: {Status}, Reintentos: {RetryCount}",
                    translationId, translation.Status, translation.RetryCount);
                return;
            }

            // Resetear estado para reintento
            translation.UpdateStatus(TranslationStatus.Queued, "Reintentando procesamiento", 0);
            await _audioRepository.UpdateAsync(translation);

            // Procesar nuevamente
            await ProcessTranslationAsync(translationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante el reintento de traducción {TranslationId}", translationId);
            throw;
        }
    }

    public async Task CleanupExpiredTranslationsAsync()
    {
        try
        {
            _logger.LogInformation("Iniciando limpieza de traducciones expiradas");

            var expiredTranslations = await _audioRepository.GetExpiredAsync();
            var cleanupCount = 0;

            foreach (var translation in expiredTranslations)
            {
                try
                {
                    // Marcar como expirada
                    translation.UpdateStatus(TranslationStatus.Expired, "Traducción expirada por tiempo límite");
                    await _audioRepository.UpdateAsync(translation);

                    // Limpiar archivos asociados
                    await CleanupTranslationFilesAsync(translation);

                    cleanupCount++;
                    _logger.LogDebug("Traducción expirada limpiada: {TranslationId}", translation.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error al limpiar traducción expirada {TranslationId}", translation.Id);
                }
            }

            _logger.LogInformation("Limpieza de traducciones expiradas completada: {CleanupCount} traducciones procesadas", cleanupCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la limpieza de traducciones expiradas");
            throw;
        }
    }

    // ===== PASOS DEL PIPELINE DE TRADUCCIÓN =====

    private async Task Step1_PrepareAudioAsync(AudioTranslation translation)
    {
        _logger.LogDebug("Paso 1: Preparando audio para traducción {TranslationId}", translation.Id);

        translation.UpdateStatus(TranslationStatus.ProcessingSpeechToText, "Preparando audio para transcripción", 25);
        await _audioRepository.UpdateAsync(translation);

        // Cargar archivo original
        var originalAudio = await _fileStorageService.LoadAudioAsync(translation.OriginalAudioPath!);

        // Convertir a formato óptimo para STT (WAV, 16kHz, mono)
        var convertedAudio = await _audioConverterService.ConvertToWavAsync(originalAudio, translation.InputFormat.ToString().ToLower());

        // Normalizar audio para mejor reconocimiento
        var normalizedAudio = await _audioProcessingService.NormalizeAudioAsync(convertedAudio, AudioFormat.Wav);

        // Guardar audio preparado
        var preparedAudioPath = await _fileStorageService.SaveAudioAsync(
            normalizedAudio,
            $"{translation.Id}_prepared.wav",
            "converted");

        translation.SetFilePaths(translatedPath: preparedAudioPath);
        await _audioRepository.UpdateAsync(translation);

        _logger.LogDebug("Audio preparado guardado: {PreparedPath}", preparedAudioPath);
    }

    private async Task Step2_SpeechToTextAsync(AudioTranslation translation)
    {
        _logger.LogDebug("Paso 2: Convirtiendo audio a texto para traducción {TranslationId}", translation.Id);

        translation.UpdateStatus(TranslationStatus.ProcessingSpeechToText, "Transcribiendo audio a texto", 40);
        await _audioRepository.UpdateAsync(translation);

        // Cargar audio preparado (o usar original si no hay preparado)
        var audioPath = translation.TranslatedAudioPath ?? translation.OriginalAudioPath!;
        var audioData = await _fileStorageService.LoadAudioAsync(audioPath);

        // Transcribir audio a texto
        var transcriptionResult = await _speechToTextService.TranscribeWithConfidenceAsync(audioData, translation.SourceLanguage);

        if (string.IsNullOrWhiteSpace(transcriptionResult.Text))
        {
            throw new InvalidOperationException("No se pudo transcribir el audio. El archivo podría no contener voz audible.");
        }

        // Guardar transcripción
        var transcriptPath = await SaveTextToFileAsync(transcriptionResult.Text, $"{translation.Id}_transcript.txt", "transcripts");
        translation.SetFilePaths(transcribedTextPath: transcriptPath);
        await _audioRepository.UpdateAsync(translation);

        _logger.LogInformation("Audio transcrito exitosamente: {TranslationId} - Texto: {TextPreview} (Confianza: {Confidence:P})",
            translation.Id, TruncateText(transcriptionResult.Text, 100), transcriptionResult.Confidence);
    }

    private async Task Step3_TranslateTextAsync(AudioTranslation translation)
    {
        _logger.LogDebug("Paso 3: Traduciendo texto para traducción {TranslationId}", translation.Id);

        translation.UpdateStatus(TranslationStatus.ProcessingTranslation, "Traduciendo texto al idioma destino", 60);
        await _audioRepository.UpdateAsync(translation);

        // Cargar texto transcrito
        var transcriptText = await LoadTextFromFileAsync(translation.TranscribedTextPath!);

        // Traducir texto
        var translationResult = await _translationService.TranslateWithConfidenceAsync(
            transcriptText,
            translation.SourceLanguage,
            translation.TargetLanguage);

        if (string.IsNullOrWhiteSpace(translationResult.TranslatedText))
        {
            throw new InvalidOperationException("No se pudo traducir el texto.");
        }

        // Guardar traducción
        var translatedTextPath = await SaveTextToFileAsync(translationResult.TranslatedText, $"{translation.Id}_translated.txt", "transcripts");
        translation.SetFilePaths(translatedTextPath: translatedTextPath);
        await _audioRepository.UpdateAsync(translation);

        _logger.LogInformation("Texto traducido exitosamente: {TranslationId} - Texto: {TextPreview} (Confianza: {Confidence:P})",
            translation.Id, TruncateText(translationResult.TranslatedText, 100), translationResult.Confidence);
    }

    private async Task Step4_TextToSpeechAsync(AudioTranslation translation)
    {
        _logger.LogDebug("Paso 4: Convirtiendo texto traducido a audio para traducción {TranslationId}", translation.Id);

        translation.UpdateStatus(TranslationStatus.ProcessingTextToSpeech, "Generando audio traducido", 80);
        await _audioRepository.UpdateAsync(translation);

        // Cargar texto traducido
        var translatedText = await LoadTextFromFileAsync(translation.TranslatedTextPath!);

        // Generar audio desde texto
        var synthesisResult = await _textToSpeechService.SynthesizeWithOptionsAsync(
            translatedText,
            translation.TargetLanguage,
            translation.Quality);

        if (synthesisResult.AudioData == null || synthesisResult.AudioData.Length == 0)
        {
            throw new InvalidOperationException("No se pudo generar audio desde el texto traducido.");
        }

        // Guardar audio final
        var finalAudioPath = await _fileStorageService.SaveAudioAsync(
            synthesisResult.AudioData,
            $"{translation.Id}_final{GetExtensionFromFormat(translation.OutputFormat)}",
            "translated_audio");

        translation.SetFilePaths(translatedPath: finalAudioPath);
        await _audioRepository.UpdateAsync(translation);

        _logger.LogInformation("Audio final generado: {TranslationId} - Tamaño: {Size} bytes, Duración: {Duration}",
            translation.Id, synthesisResult.AudioData.Length, synthesisResult.Duration);
    }

    private async Task Step5_FinalizeAsync(AudioTranslation translation)
    {
        _logger.LogDebug("Paso 5: Finalizando traducción {TranslationId}", translation.Id);

        translation.UpdateStatus(TranslationStatus.Completed, "Traducción completada exitosamente", 100);
        await _audioRepository.UpdateAsync(translation);

        _logger.LogInformation("Traducción finalizada exitosamente: {TranslationId}", translation.Id);
    }

    // ===== MÉTODOS AUXILIARES =====

    private async Task<string> SaveTextToFileAsync(string text, string fileName, string directory)
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
        return await _fileStorageService.SaveAudioAsync(textBytes, fileName, directory);
    }

    private async Task<string> LoadTextFromFileAsync(string filePath)
    {
        var textBytes = await _fileStorageService.LoadAudioAsync(filePath);
        return System.Text.Encoding.UTF8.GetString(textBytes);
    }

    private async Task CleanupTranslationFilesAsync(AudioTranslation translation)
    {
        var filePaths = new[]
        {
            translation.OriginalAudioPath,
            translation.TranslatedAudioPath,
            translation.TranscribedTextPath,
            translation.TranslatedTextPath
        };

        foreach (var filePath in filePaths.Where(p => !string.IsNullOrEmpty(p)))
        {
            try
            {
                await _fileStorageService.DeleteFileAsync(filePath!);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo eliminar archivo: {FilePath}", filePath);
            }
        }
    }

    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength) + "...";
    }

    private string GetExtensionFromFormat(AudioFormat format)
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
}