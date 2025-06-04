using AudioTranslationAPI.Domain.Types;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.Transactions;

namespace AudioTranslationAPI.Application.DTOs
{
    // Request DTO
    // Request DTO para Application Layer
    public class TranslationRequestDto
    {
        //[Required]
        public byte[] AudioData { get; set; } = Array.Empty<byte>();

        //[Required]
        public string FileName { get; set; } = string.Empty;

        //[Required]
        public string ContentType { get; set; } = string.Empty;

        //[Required]
        [StringLength(5, MinimumLength = 2)]
        public string SourceLanguage { get; set; } = "es";

        //[Required]
        [StringLength(5, MinimumLength = 2)]
        public string TargetLanguage { get; set; } = "en";

        public string? UserId { get; set; }

        public long FileSizeBytes { get; set; }
    }

    // Request DTO para API Layer (con IFormFile)
    public class ApiTranslationRequestDto
    {
        [Required]
        public IFormFile AudioFile { get; set; } = null!; // ← AGREGAR ESTA LÍNEA

        [Required]
        public string SourceLanguage { get; set; } = "es";

        [Required]
        public string TargetLanguage { get; set; } = "en";

        public string? UserId { get; set; }
    }

    // Response DTO
    public class TranslationResponseDto
    {
        public Guid TranslationId { get; set; }
        public TranslationStatus Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? EstimatedCompletionTime { get; set; }
    }

    // Status DTO
    public class TranslationStatusDto
    {
        public Guid TranslationId { get; set; }
        public TranslationStatus Status { get; set; }
        public int Progress { get; set; } // 0-100
        public string CurrentStep { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? EstimatedTimeRemaining { get; set; }
    }

    // Audio Result DTO
    public class TranslatedAudioResultDto
    {
        public Guid TranslationId { get; set; }
        public bool IsCompleted { get; set; }
        public TranslationStatus Status { get; set; }
        public int Progress { get; set; }
        public byte[]? AudioData { get; set; }
        public string ContentType { get; set; } = "audio/wav";
        public string FileName { get; set; } = string.Empty;
        public long? FileSizeBytes { get; set; }
    }
}
