using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.Types;
using AudioTranslationAPI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;

namespace AudioTranslationAPI.Infrastructure.ExternalServices;

/// <summary>
/// Servicio para conversión de texto a audio usando Google Cloud Text-to-Speech API
/// </summary>
public class GoogleTextToSpeechService : ITextToSpeechService
{
    private readonly ILogger<GoogleTextToSpeechService> _logger;
    private readonly GoogleTextToSpeechOptions _options;
    private readonly HttpClient _httpClient;

    // Configuración de voces por idioma
    private readonly Dictionary<string, VoiceConfiguration> _voiceConfigurations = new()
    {
        {
            "es", new VoiceConfiguration
            {
                LanguageCode = "es-ES",
                VoiceNames = new[] { "es-ES-Standard-A", "es-ES-Wavenet-B", "es-ES-Neural2-C" },
                DefaultVoice = "es-ES-Wavenet-B"
            }
        },
        {
            "en", new VoiceConfiguration
            {
                LanguageCode = "en-US",
                VoiceNames = new[] { "en-US-Standard-C", "en-US-Wavenet-D", "en-US-Neural2-F" },
                DefaultVoice = "en-US-Wavenet-D"
            }
        },
        {
            "fr", new VoiceConfiguration
            {
                LanguageCode = "fr-FR",
                VoiceNames = new[] { "fr-FR-Standard-A", "fr-FR-Wavenet-C" },
                DefaultVoice = "fr-FR-Wavenet-C"
            }
        },
        {
            "pt", new VoiceConfiguration
            {
                LanguageCode = "pt-BR",
                VoiceNames = new[] { "pt-BR-Standard-A", "pt-BR-Wavenet-A" },
                DefaultVoice = "pt-BR-Wavenet-A"
            }
        },
        {
            "it", new VoiceConfiguration
            {
                LanguageCode = "it-IT",
                VoiceNames = new[] { "it-IT-Standard-A", "it-IT-Wavenet-A" },
                DefaultVoice = "it-IT-Wavenet-A"
            }
        },
        {
            "de", new VoiceConfiguration
            {
                LanguageCode = "de-DE",
                VoiceNames = new[] { "de-DE-Standard-A", "de-DE-Wavenet-F" },
                DefaultVoice = "de-DE-Wavenet-F"
            }
        }
    };

    public GoogleTextToSpeechService(
        ILogger<GoogleTextToSpeechService> logger,
        IOptions<ExternalServicesOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options.Value.Google.TextToSpeech;
        _httpClient = httpClient;

        // Configurar HttpClient
        _httpClient.BaseAddress = new Uri("https://texttospeech.googleapis.com/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AudioTranslationAPI/1.0");
    }

    public async Task<byte[]> SynthesizeAsync(string text, string languageCode, string? voiceId = null)
    {
        var result = await SynthesizeWithOptionsAsync(text, languageCode, AudioQuality.Standard, voiceId);
        return result.AudioData;
    }

    public async Task<AudioSynthesisResult> SynthesizeWithOptionsAsync(string text, string languageCode, AudioQuality quality, string? voiceId = null)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.ApiKey))
            {
                throw new InvalidOperationException("Google Text-to-Speech API Key no configurada");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("El texto no puede estar vacío", nameof(text));
            }

            _logger.LogDebug("Iniciando síntesis de voz con Google TTS: {TextLength} caracteres, Idioma: {Language}, Calidad: {Quality}",
                text.Length, languageCode, quality);

            // Validar longitud del texto (Google tiene límite de 5000 caracteres)
            if (text.Length > 5000)
            {
                _logger.LogInformation("Texto largo detectado ({Length} caracteres), dividiendo en chunks", text.Length);
                return await SynthesizeLongTextAsync(text, languageCode, quality, voiceId);
            }

            var voiceConfig = GetVoiceConfiguration(languageCode);
            var selectedVoice = voiceId ?? voiceConfig.DefaultVoice;
            var audioConfig = GetAudioConfig(quality);

            // Crear request para Google Cloud Text-to-Speech API
            var requestBody = new
            {
                input = new
                {
                    text = text
                },
                voice = new
                {
                    languageCode = voiceConfig.LanguageCode,
                    name = selectedVoice
                },
                audioConfig = audioConfig
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Realizar petición a Google Cloud Text-to-Speech
            var requestUrl = $"v1/text:synthesize?key={_options.ApiKey}";
            var response = await _httpClient.PostAsync(requestUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error en Google Text-to-Speech API: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Error en Google Text-to-Speech: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var ttsResponse = JsonSerializer.Deserialize<GoogleTTSResponse>(responseContent,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (string.IsNullOrEmpty(ttsResponse?.AudioContent))
            {
                throw new InvalidOperationException("Google Text-to-Speech no devolvió contenido de audio");
            }

            var audioData = Convert.FromBase64String(ttsResponse.AudioContent);
            var estimatedDuration = EstimateAudioDuration(text, languageCode);

            var result = new AudioSynthesisResult
            {
                AudioData = audioData,
                Format = AudioFormat.Mp3, // Google TTS devuelve MP3 por defecto
                Duration = estimatedDuration,
                ContentType = "audio/mpeg"
            };

            _logger.LogInformation("Síntesis de voz completada: {AudioSize} bytes, Duración estimada: {Duration}",
                audioData.Length, estimatedDuration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la síntesis de voz con Google TTS");
            throw new InvalidOperationException($"Error en síntesis de voz: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<VoiceInfo>> GetAvailableVoicesAsync(string languageCode)
    {
        try
        {
            if (!_voiceConfigurations.TryGetValue(languageCode.ToLowerInvariant(), out var voiceConfig))
            {
                return Enumerable.Empty<VoiceInfo>();
            }

            // En un escenario real, consultaríamos la API de Google para obtener voces disponibles
            // Por ahora, devolvemos las voces configuradas localmente
            var voices = voiceConfig.VoiceNames.Select(voiceName => new VoiceInfo
            {
                Id = voiceName,
                Name = GetFriendlyVoiceName(voiceName),
                Gender = DetermineGender(voiceName),
                LanguageCode = voiceConfig.LanguageCode
            });

            return await Task.FromResult(voices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener voces disponibles para idioma {LanguageCode}", languageCode);
            return Enumerable.Empty<VoiceInfo>();
        }
    }

    // ===== MÉTODOS AUXILIARES =====

    private VoiceConfiguration GetVoiceConfiguration(string languageCode)
    {
        var normalizedCode = languageCode.ToLowerInvariant();

        if (_voiceConfigurations.TryGetValue(normalizedCode, out var config))
        {
            return config;
        }

        // Fallback para idiomas no configurados
        _logger.LogWarning("Configuración de voz no encontrada para idioma {LanguageCode}, usando configuración por defecto", languageCode);
        return new VoiceConfiguration
        {
            LanguageCode = "en-US",
            VoiceNames = new[] { "en-US-Standard-C" },
            DefaultVoice = "en-US-Standard-C"
        };
    }

    private object GetAudioConfig(AudioQuality quality)
    {
        return quality switch
        {
            AudioQuality.Low => new
            {
                audioEncoding = "MP3",
                sampleRateHertz = 16000,
                effectsProfileId = new[] { "handset-class-device" }
            },
            AudioQuality.Standard => new
            {
                audioEncoding = "MP3",
                sampleRateHertz = 22050,
                effectsProfileId = new[] { "wearable-class-device" }
            },
            AudioQuality.High => new
            {
                audioEncoding = "MP3",
                sampleRateHertz = 24000,
                effectsProfileId = new[] { "headphone-class-device" }
            },
            AudioQuality.Premium => new
            {
                audioEncoding = "LINEAR16",
                sampleRateHertz = 44100,
                effectsProfileId = new[] { "large-home-entertainment-class-device" }
            },
            _ => new
            {
                audioEncoding = "MP3",
                sampleRateHertz = 22050
            }
        };
    }

    private async Task<AudioSynthesisResult> SynthesizeLongTextAsync(string text, string languageCode, AudioQuality quality, string? voiceId)
    {
        const int maxChunkSize = 4000; // 4k caracteres por chunk para estar seguros
        var chunks = SplitTextIntoChunks(text, maxChunkSize);
        var audioChunks = new List<byte[]>();
        var totalDuration = TimeSpan.Zero;

        _logger.LogInformation("Sintetizando texto largo en {ChunkCount} chunks", chunks.Count);

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            try
            {
                var chunkResult = await SynthesizeWithOptionsAsync(chunk, languageCode, quality, voiceId);
                audioChunks.Add(chunkResult.AudioData);
                totalDuration = totalDuration.Add(chunkResult.Duration);

                _logger.LogDebug("Chunk {Index}/{Total} sintetizado: {Size} bytes", index + 1, chunks.Count, chunkResult.AudioData.Length);

                // Pequeña pausa para no sobrecargar la API
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al sintetizar chunk {Index}/{Total}", index + 1, chunks.Count);
                throw;
            }
        }

        // Combinar todos los chunks de audio
        var combinedAudio = CombineAudioChunks(audioChunks);

        return new AudioSynthesisResult
        {
            AudioData = combinedAudio,
            Format = AudioFormat.Mp3,
            Duration = totalDuration,
            ContentType = "audio/mpeg"
        };
    }

    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        var sentences = text.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            // Si agregar esta oración excede el límite y ya tenemos contenido, crear nuevo chunk
            if (currentChunk.Length + trimmedSentence.Length > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            currentChunk.Append(trimmedSentence);

            // Agregar puntuación apropiada si no la tiene
            if (!trimmedSentence.EndsWith('.') && !trimmedSentence.EndsWith('!') && !trimmedSentence.EndsWith('?'))
            {
                currentChunk.Append(". ");
            }
            else
            {
                currentChunk.Append(" ");
            }
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private byte[] CombineAudioChunks(List<byte[]> audioChunks)
    {
        // Nota: Esta es una combinación simple. En producción, sería mejor usar FFmpeg
        // para combinar archivos MP3 correctamente respetando headers y estructura
        var totalSize = audioChunks.Sum(chunk => chunk.Length);
        var combined = new byte[totalSize];
        var offset = 0;

        foreach (var chunk in audioChunks)
        {
            Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
            offset += chunk.Length;
        }

        return combined;
    }

    private TimeSpan EstimateAudioDuration(string text, string languageCode)
    {
        // Estimación basada en velocidad promedio de habla por idioma
        var wordsPerMinute = languageCode.ToLowerInvariant() switch
        {
            "es" => 150,  // Español
            "en" => 160,  // Inglés
            "fr" => 140,  // Francés
            "pt" => 145,  // Portugués
            "it" => 155,  // Italiano
            "de" => 130,  // Alemán
            _ => 150      // Default
        };

        var wordCount = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var minutes = (double)wordCount / wordsPerMinute;

        return TimeSpan.FromMinutes(minutes);
    }

    private string GetFriendlyVoiceName(string voiceName)
    {
        // Convertir nombres técnicos a nombres amigables
        return voiceName switch
        {
            var name when name.Contains("Standard") => name.Replace("Standard", "Estándar"),
            var name when name.Contains("Wavenet") => name.Replace("Wavenet", "Premium"),
            var name when name.Contains("Neural2") => name.Replace("Neural2", "Neural"),
            _ => voiceName
        };
    }

    private string DetermineGender(string voiceName)
    {
        // Determinación simple basada en el sufijo de la voz
        return voiceName switch
        {
            var name when name.EndsWith("-A") || name.EndsWith("-C") || name.EndsWith("-F") => "Femenino",
            var name when name.EndsWith("-B") || name.EndsWith("-D") => "Masculino",
            _ => "Neutral"
        };
    }
}

// ===== CLASES AUXILIARES =====

public class VoiceConfiguration
{
    public string LanguageCode { get; set; } = string.Empty;
    public string[] VoiceNames { get; set; } = Array.Empty<string>();
    public string DefaultVoice { get; set; } = string.Empty;
}

// ===== CLASES PARA DESERIALIZACIÓN =====

public class GoogleTTSResponse
{
    public string? AudioContent { get; set; }
}