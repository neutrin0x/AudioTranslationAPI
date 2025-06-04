using AudioTranslationAPI.Domain.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioTranslationAPI.Domain.Entities
{
    /// <summary>
    /// Entidad principal que representa una traducción de audio
    /// </summary>
    public class AudioTranslation
    {
        public Guid Id { get; private set; }
        public string? UserId { get; private set; }
        public string SourceLanguage { get; private set; }
        public string TargetLanguage { get; private set; }
        public TranslationStatus Status { get; private set; }
        public int Progress { get; private set; }
        public string CurrentStep { get; private set; }
        public AudioFormat InputFormat { get; private set; }
        public AudioFormat OutputFormat { get; private set; }
        public AudioQuality Quality { get; private set; }
        public ProcessingPriority Priority { get; private set; }

        // Metadatos del archivo original
        public string OriginalFileName { get; private set; }
        public long OriginalFileSizeBytes { get; private set; }
        public TimeSpan OriginalDuration { get; private set; }

        // Paths de archivos
        public string? OriginalAudioPath { get; private set; }
        public string? TranslatedAudioPath { get; private set; }
        public string? TranscribedTextPath { get; private set; }
        public string? TranslatedTextPath { get; private set; }

        // Timestamps
        public DateTime CreatedAt { get; private set; }
        public DateTime? StartedAt { get; private set; }
        public DateTime? CompletedAt { get; private set; }
        public DateTime? ExpiresAt { get; private set; }

        // Error handling
        public string? ErrorMessage { get; private set; }
        public string? ErrorDetails { get; private set; }
        public int RetryCount { get; private set; }

        // Métricas
        public TimeSpan? ProcessingDuration { get; private set; }
        public long? OutputFileSizeBytes { get; private set; }

        // Constructor privado para EF Core
        private AudioTranslation()
        {
            SourceLanguage = string.Empty;
            TargetLanguage = string.Empty;
            CurrentStep = string.Empty;
            OriginalFileName = string.Empty;
        }

        // Factory method
        public static AudioTranslation Create(
            string sourceLanguage,
            string targetLanguage,
            string originalFileName,
            long originalFileSizeBytes,
            TimeSpan originalDuration,
            AudioFormat inputFormat,
            string? userId = null,
            AudioQuality quality = AudioQuality.Standard,
            ProcessingPriority priority = ProcessingPriority.Normal)
        {
            var translation = new AudioTranslation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                Status = TranslationStatus.Queued,
                Progress = 0,
                CurrentStep = "En cola para procesamiento",
                InputFormat = inputFormat,
                OutputFormat = AudioFormat.Wav, // Default output
                Quality = quality,
                Priority = priority,
                OriginalFileName = originalFileName,
                OriginalFileSizeBytes = originalFileSizeBytes,
                OriginalDuration = originalDuration,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24), // Expira en 24 horas
                RetryCount = 0
            };

            return translation;
        }

        // Métodos de dominio
        public void UpdateStatus(TranslationStatus status, string currentStep, int progress = 0)
        {
            Status = status;
            CurrentStep = currentStep;
            Progress = Math.Clamp(progress, 0, 100);

            if (status == TranslationStatus.ProcessingSpeechToText && StartedAt == null)
            {
                StartedAt = DateTime.UtcNow;
            }

            if (status == TranslationStatus.Completed)
            {
                CompletedAt = DateTime.UtcNow;
                Progress = 100;
                ProcessingDuration = StartedAt.HasValue ? DateTime.UtcNow - StartedAt.Value : null;
            }
        }

        public void SetError(string errorMessage, string? errorDetails = null)
        {
            Status = TranslationStatus.Failed;
            ErrorMessage = errorMessage;
            ErrorDetails = errorDetails;
            CurrentStep = "Error en el procesamiento";
        }

        public void SetFilePaths(string? originalPath = null, string? translatedPath = null,
                               string? transcribedTextPath = null, string? translatedTextPath = null)
        {
            if (!string.IsNullOrEmpty(originalPath))
                OriginalAudioPath = originalPath;

            if (!string.IsNullOrEmpty(translatedPath))
                TranslatedAudioPath = translatedPath;

            if (!string.IsNullOrEmpty(transcribedTextPath))
                TranscribedTextPath = transcribedTextPath;

            if (!string.IsNullOrEmpty(translatedTextPath))
                TranslatedTextPath = translatedTextPath;
        }

        public void IncrementRetry()
        {
            RetryCount++;
        }

        public void Cancel()
        {
            if (Status != TranslationStatus.Completed && Status != TranslationStatus.Failed)
            {
                Status = TranslationStatus.Cancelled;
                CurrentStep = "Cancelado por el usuario";
            }
        }

        public bool IsExpired()
        {
            return DateTime.UtcNow > ExpiresAt;
        }

        public bool CanBeRetried()
        {
            return Status == TranslationStatus.Failed && RetryCount < 3;
        }
    }
}
