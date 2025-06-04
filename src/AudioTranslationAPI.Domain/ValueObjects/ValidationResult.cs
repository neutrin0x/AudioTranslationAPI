namespace AudioTranslationAPI.Domain.ValueObjects;

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public AudioMetadata? Metadata { get; set; }

    // Constructor para resultado válido
    public static ValidationResult Success(AudioMetadata metadata)
    {
        return new ValidationResult
        {
            IsValid = true,
            Metadata = metadata,
            Errors = new List<string>()
        };
    }

    // Constructor para resultado inválido
    public static ValidationResult Failure(params string[] errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors.ToList(),
            Metadata = null
        };
    }

    // Constructor para resultado inválido con lista de errores
    public static ValidationResult Failure(List<string> errors)
    {
        return new ValidationResult
        {
            IsValid = false,
            Errors = errors,
            Metadata = null
        };
    }
}
