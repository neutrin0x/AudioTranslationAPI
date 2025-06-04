using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;

namespace AudioTranslationAPI.Infrastructure.ExternalServices;

/// <summary>
/// Servicio para conversión de audio a texto usando Google Cloud Speech-to-Text API
/// </summary>
public class GoogleSpeechToTextService : ISpeechToTextService
{
    private readonly ILogger<GoogleSpeechToTextService> _logger;
    private readonly GoogleSpeechToTextOptions _options;
    private readonly HttpClient _httpClient;

    // Mapeo de códigos de idioma a Google Cloud
    private readonly Dictionary<string, string> _languageMapping = new()
    {
        { "es", "es-ES" },      // Español (España)
        { "es-ar", "es-AR" },   // Español (Argentina)
        { "es-mx", "es-MX" },   // Español (México)
        { "en", "en-US" },      // Inglés (Estados Unidos)
        { "en-gb", "en-GB" },   // Inglés (Reino Unido)
        { "fr", "fr-FR" },      // Francés (Francia)
        { "pt", "pt-BR" },      // Portugués (Brasil)
        { "it", "it-IT" },      // Italiano (Italia)
        { "de", "de-DE" }       // Alemán (Alemania)
    };

    public GoogleSpeechToTextService(
        ILogger<GoogleSpeechToTextService> logger,
        IOptions<ExternalServicesOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options.Value.Google.SpeechToText;
        _httpClient = httpClient;

        // Configurar HttpClient
        _httpClient.BaseAddress = new Uri("https://speech.googleapis.com/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AudioTranslationAPI/1.0");
    }

    public async Task<string> TranscribeAsync(byte[] audioData, string languageCode)
    {
        var result = await TranscribeWithConfidenceAsync(audioData, languageCode);
        return result.Text;
    }

    public async Task<TranscriptionResult> TranscribeWithConfidenceAsync(byte[] audioData, string languageCode)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.ApiKey))
            {
                throw new InvalidOperationException("Google Speech-to-Text API Key no configurada");
            }

            _logger.LogDebug("Iniciando transcripción con Google STT: {AudioSize} bytes, Idioma: {Language}",
                audioData.Length, languageCode);

            // Validar tamaño del archivo (Google tiene límite de 10MB para requests síncronos)
            if (audioData.Length > 10 * 1024 * 1024)
            {
                throw new ArgumentException("El archivo es demasiado grande para transcripción síncrona (máximo 10MB)");
            }

            var googleLanguageCode = GetGoogleLanguageCode(languageCode);
            var base64Audio = Convert.ToBase64String(audioData);

            // Crear request para Google Cloud Speech-to-Text API
            var requestBody = new
            {
                config = new
                {
                    encoding = "MP3", // "WEBM_OPUS",
                    sampleRateHertz = 16000,
                    languageCode = googleLanguageCode,
                    alternativeLanguageCodes = GetAlternativeLanguages(googleLanguageCode),
                    enableAutomaticPunctuation = true,
                    enableWordTimeOffsets = false,
                    enableWordConfidence = true,
                    model = "latest_long", // Modelo optimizado para audio largo
                    useEnhanced = true
                },
                audio = new
                {
                    content = base64Audio
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Realizar petición a Google Cloud Speech-to-Text
            var requestUrl = $"v1/speech:recognize?key={_options.ApiKey}";
            var response = await _httpClient.PostAsync(requestUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error en Google Speech-to-Text API: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Error en Google Speech-to-Text: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var speechResponse = JsonSerializer.Deserialize<GoogleSpeechResponse>(responseContent,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (speechResponse?.Results == null || !speechResponse.Results.Any())
            {
                _logger.LogWarning("Google Speech-to-Text no devolvió resultados para el audio");
                return new TranscriptionResult
                {
                    Text = "",
                    Confidence = 0.0f,
                    Duration = TimeSpan.Zero,
                    LanguageDetected = googleLanguageCode
                };
            }

            // Procesar resultados
            var bestResult = speechResponse.Results.First();
            var bestAlternative = bestResult.Alternatives?.FirstOrDefault();

            if (bestAlternative == null)
            {
                throw new InvalidOperationException("No se encontraron alternativas en la respuesta de Google Speech-to-Text");
            }

            var result = new TranscriptionResult
            {
                Text = bestAlternative.Transcript ?? "",
                Confidence = bestAlternative.Confidence ?? 0.0f,
                Duration = TimeSpan.Zero, // Google no devuelve duración total en esta API
                LanguageDetected = speechResponse.Results.FirstOrDefault()?.LanguageCode ?? googleLanguageCode
            };

            _logger.LogInformation("Transcripción completada: {TextLength} caracteres, Confianza: {Confidence:P}",
                result.Text.Length, result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la transcripción con Google Speech-to-Text");
            throw new InvalidOperationException($"Error en transcripción: {ex.Message}", ex);
        }
    }

    public bool IsLanguageSupported(string languageCode)
    {
        return _languageMapping.ContainsKey(languageCode.ToLowerInvariant());
    }

    // ===== MÉTODOS AUXILIARES =====

    private string GetGoogleLanguageCode(string languageCode)
    {
        var normalizedCode = languageCode.ToLowerInvariant();

        if (_languageMapping.TryGetValue(normalizedCode, out var googleCode))
        {
            return googleCode;
        }

        // Fallback: intentar usar el código tal como viene
        _logger.LogWarning("Código de idioma no mapeado: {LanguageCode}, usando como está", languageCode);
        return languageCode;
    }

    private string[] GetAlternativeLanguages(string primaryLanguage)
    {
        // Proporcionar idiomas alternativos para mejor detección
        return primaryLanguage switch
        {
            "es-ES" => new[] { "es-AR", "es-MX" },
            "en-US" => new[] { "en-GB" },
            "pt-BR" => new[] { "pt-PT" },
            _ => Array.Empty<string>()
        };
    }
}

// ===== CLASES PARA DESERIALIZACIÓN =====

public class GoogleSpeechResponse
{
    public GoogleSpeechResult[]? Results { get; set; }
}

public class GoogleSpeechResult
{
    public GoogleSpeechAlternative[]? Alternatives { get; set; }
    public string? LanguageCode { get; set; }
}

public class GoogleSpeechAlternative
{
    public string? Transcript { get; set; }
    public float? Confidence { get; set; }
    public GoogleWordInfo[]? Words { get; set; }
}

public class GoogleWordInfo
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Word { get; set; }
    public float? Confidence { get; set; }
}