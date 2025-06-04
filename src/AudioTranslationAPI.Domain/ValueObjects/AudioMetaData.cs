using AudioTranslationAPI.Domain.Types;

namespace AudioTranslationAPI.Domain.ValueObjects;

public class AudioMetadata
{
    public TimeSpan Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitRate { get; set; }
    public AudioFormat Format { get; set; }
    public long FileSizeBytes { get; set; }
}
