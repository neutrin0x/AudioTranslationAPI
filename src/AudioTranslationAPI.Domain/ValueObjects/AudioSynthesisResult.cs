using AudioTranslationAPI.Domain.Types;

namespace AudioTranslationAPI.Domain.ValueObjects;

public class AudioSynthesisResult
{
    public byte[] AudioData { get; set; } = Array.Empty<byte>();
    public AudioFormat Format { get; set; }
    public TimeSpan Duration { get; set; }
    public string ContentType { get; set; } = string.Empty;
}
