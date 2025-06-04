using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioTranslationAPI.Application.Configuration;

/// <summary>
/// Configuración para procesamiento de audio
/// </summary>
public class AudioProcessingOptions
{
    public string FFmpegPath { get; set; } = "/usr/bin/ffmpeg";
    public string TempDirectory { get; set; } = "./temp";
    public string OutputDirectory { get; set; } = "./output";
    public int MaxFileSizeMB { get; set; } = 50;
    public int MaxDurationMinutes { get; set; } = 10;
    public string[] SupportedInputFormats { get; set; } = { "wav", "mp3", "ogg", "aac", "flac" };
    public string DefaultOutputFormat { get; set; } = "wav";
    public int DefaultSampleRate { get; set; } = 16000;
    public int DefaultBitRate { get; set; } = 128000;
    public int CleanupTempFilesAfterHours { get; set; } = 24;
}

/// <summary>
/// Configuración para servicios externos
/// </summary>
public class ExternalServicesOptions
{
    public GoogleServicesOptions Google { get; set; } = new();
    public OpenAIServicesOptions OpenAI { get; set; } = new();
    public AzureServicesOptions Azure { get; set; } = new();
    public MyMemoryTranslationOptions MyMemory { get; set; } = new();
}

public class GoogleServicesOptions
{
    public GoogleSpeechToTextOptions SpeechToText { get; set; } = new();
    public GoogleTranslationOptions Translation { get; set; } = new();
    public GoogleTextToSpeechOptions TextToSpeech { get; set; } = new();
}

public class GoogleSpeechToTextOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string CredentialsPath { get; set; } = string.Empty;
}

public class GoogleTranslationOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
}

public class GoogleTextToSpeechOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
}

public class OpenAIServicesOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string WhisperModel { get; set; } = "whisper-1";
    public string TranslationModel { get; set; } = "gpt-4";
    public string TTSModel { get; set; } = "tts-1";
}

public class AzureServicesOptions
{
    public string SpeechKey { get; set; } = string.Empty;
    public string SpeechRegion { get; set; } = "eastus";
    public string TranslatorKey { get; set; } = string.Empty;
    public string TranslatorRegion { get; set; } = "global";
}

public class MyMemoryTranslationOptions
{
    public string Email { get; set; } = string.Empty;
}