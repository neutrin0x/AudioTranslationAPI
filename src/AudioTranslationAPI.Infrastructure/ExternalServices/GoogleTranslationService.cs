using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using System.Web;

namespace AudioTranslationAPI.Infrastructure.ExternalServices;

/// <summary>
/// Servicio para traducción de texto usando Google Cloud Translation API
/// </summary>
public class GoogleTranslationService : ITranslationService
{
    private readonly ILogger<GoogleTranslationService> _logger;
    private readonly GoogleTranslationOptions _options;
    private readonly HttpClient _httpClient;

    // Mapeo de códigos de idioma para Google Translate
    private readonly Dictionary<string, string> _languageMapping = new()
    {
        { "es", "es" },    // Español
        { "en", "en" },    // Inglés
        { "fr", "fr" },    // Francés
        { "pt", "pt" },    // Portugués
        { "it", "it" },    // Italiano
        { "de", "de" },    // Alemán
        { "ja", "ja" },    // Japonés
        { "ko", "ko" },    // Coreano
        { "zh", "zh" },    // Chino
        { "ru", "ru" },    // Ruso
        { "ar", "ar" },    // Árabe
        { "hi", "hi" },    // Hindi
    };

    // Pares de idiomas con buena calidad de traducción
    private readonly HashSet<(string, string)> _supportedPairs = new()
    {
        ("es", "en"), ("en", "es"),  // Español ↔ Inglés
        ("es", "fr"), ("fr", "es"),  // Español ↔ Francés
        ("es", "pt"), ("pt", "es"),  // Español ↔ Portugués
        ("en", "fr"), ("fr", "en"),  // Inglés ↔ Francés
        ("en", "de"), ("de", "en"),  // Inglés ↔ Alemán
        ("en", "it"), ("it", "en"),  // Inglés ↔ Italiano
    };

    public GoogleTranslationService(
        ILogger<GoogleTranslationService> logger,
        IOptions<ExternalServicesOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options.Value.Google.Translation;
        _httpClient = httpClient;

        // Configurar HttpClient
        _httpClient.BaseAddress = new Uri("https://translation.googleapis.com/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AudioTranslationAPI/1.0");
    }

    public async Task<string> TranslateAsync(string text, string fromLanguage, string toLanguage)
    {
        var result = await TranslateWithConfidenceAsync(text, fromLanguage, toLanguage);
        return result.TranslatedText;
    }

    public async Task<TranslationResult> TranslateWithConfidenceAsync(string text, string fromLanguage, string toLanguage)
    {
        try
        {
            if (string.IsNullOrEmpty(_options.ApiKey))
            {
                throw new InvalidOperationException("Google Translation API Key no configurada");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return new TranslationResult
                {
                    TranslatedText = "",
                    Confidence = 1.0f,
                    DetectedSourceLanguage = fromLanguage
                };
            }

            _logger.LogDebug("Iniciando traducción con Google Translate: {TextLength} caracteres, {FromLang} → {ToLang}",
                text.Length, fromLanguage, toLanguage);

            // Validar longitud del texto (Google tiene límites)
            if (text.Length > 30000) // 30k caracteres es un límite razonable
            {
                _logger.LogWarning("Texto muy largo para traducción: {Length} caracteres", text.Length);
                // Dividir en chunks si es necesario
                return await TranslateLongTextAsync(text, fromLanguage, toLanguage);
            }

            var googleFromLang = GetGoogleLanguageCode(fromLanguage);
            var googleToLang = GetGoogleLanguageCode(toLanguage);

            // Crear request para Google Cloud Translation API v3
            var requestBody = new
            {
                q = text,
                source = googleFromLang,
                target = googleToLang,
                format = "text"
            };

            var queryString = $"q={HttpUtility.UrlEncode(text)}" +
                            $"&source={googleFromLang}" +
                            $"&target={googleToLang}" +
                            $"&format=text" +
                            $"&key={_options.ApiKey}";

            // Realizar petición a Google Cloud Translation API
            var requestUrl = $"language/translate/v2?{queryString}";
            var response = await _httpClient.PostAsync(requestUrl, null);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error en Google Translation API: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Error en Google Translation: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var translationResponse = JsonSerializer.Deserialize<GoogleTranslationResponse>(responseContent,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (translationResponse?.Data?.Translations == null || !translationResponse.Data.Translations.Any())
            {
                throw new InvalidOperationException("Google Translation no devolvió resultados");
            }

            var translation = translationResponse.Data.Translations.First();

            var result = new TranslationResult
            {
                TranslatedText = translation.TranslatedText ?? "",
                Confidence = CalculateConfidence(text, translation.TranslatedText ?? ""),
                DetectedSourceLanguage = translation.DetectedSourceLanguage ?? fromLanguage
            };

            _logger.LogInformation("Traducción completada: {OriginalLength} → {TranslatedLength} caracteres, Confianza estimada: {Confidence:P}",
                text.Length, result.TranslatedText.Length, result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la traducción con Google Translate");
            throw new InvalidOperationException($"Error en traducción: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsLanguagePairSupportedAsync(string fromLanguage, string toLanguage)
    {
        try
        {
            var googleFromLang = GetGoogleLanguageCode(fromLanguage);
            var googleToLang = GetGoogleLanguageCode(toLanguage);

            // Verificar en nuestro mapeo local primero
            if (_supportedPairs.Contains((googleFromLang, googleToLang)))
            {
                return true;
            }

            // Si no está en el mapeo local, consultar a Google (cache este resultado en producción)
            return await CheckLanguageSupportAsync(googleFromLang, googleToLang);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar soporte de idiomas {FromLang} → {ToLang}", fromLanguage, toLanguage);
            return false;
        }
    }

    // ===== MÉTODOS AUXILIARES =====

    private string GetGoogleLanguageCode(string languageCode)
    {
        var normalizedCode = languageCode.ToLowerInvariant();

        if (_languageMapping.TryGetValue(normalizedCode, out var googleCode))
        {
            return googleCode;
        }

        // Fallback: usar el código tal como viene
        _logger.LogWarning("Código de idioma no mapeado: {LanguageCode}, usando como está", languageCode);
        return languageCode;
    }

    private float CalculateConfidence(string originalText, string translatedText)
    {
        // Estimación simple de confianza basada en características del texto
        if (string.IsNullOrWhiteSpace(translatedText))
            return 0.0f;

        var confidence = 0.8f; // Confianza base para Google Translate

        // Ajustar basado en la longitud relativa
        var lengthRatio = (float)translatedText.Length / originalText.Length;
        if (lengthRatio > 0.3f && lengthRatio < 3.0f) // Ratio razonable
        {
            confidence += 0.1f;
        }

        // Ajustar si tiene caracteres especiales o números preservados
        if (originalText.Any(char.IsDigit) && translatedText.Any(char.IsDigit))
        {
            confidence += 0.05f;
        }

        return Math.Min(confidence, 1.0f);
    }

    private async Task<TranslationResult> TranslateLongTextAsync(string text, string fromLanguage, string toLanguage)
    {
        const int chunkSize = 5000; // 5k caracteres por chunk
        var chunks = SplitTextIntoChunks(text, chunkSize);
        var translatedChunks = new List<string>();
        var totalConfidence = 0.0f;
        var detectedLanguage = fromLanguage;

        _logger.LogInformation("Traduciendo texto largo en {ChunkCount} chunks", chunks.Count);

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            try
            {
                var chunkResult = await TranslateWithConfidenceAsync(chunk, fromLanguage, toLanguage);
                translatedChunks.Add(chunkResult.TranslatedText);
                totalConfidence += chunkResult.Confidence;

                if (index == 0) // Usar el idioma detectado del primer chunk
                {
                    detectedLanguage = chunkResult.DetectedSourceLanguage;
                }

                // Pequeña pausa para no sobrecargar la API
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al traducir chunk {Index}/{Total}", index + 1, chunks.Count);
                throw;
            }
        }

        return new TranslationResult
        {
            TranslatedText = string.Join(" ", translatedChunks),
            Confidence = totalConfidence / chunks.Count,
            DetectedSourceLanguage = detectedLanguage
        };
    }

    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new StringBuilder();

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            if (currentChunk.Length + trimmedSentence.Length > maxChunkSize && currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
                currentChunk.Clear();
            }

            currentChunk.Append(trimmedSentence).Append(". ");
        }

        if (currentChunk.Length > 0)
        {
            chunks.Add(currentChunk.ToString().Trim());
        }

        return chunks;
    }

    private async Task<bool> CheckLanguageSupportAsync(string fromLanguage, string toLanguage)
    {
        try
        {
            // Consultar idiomas soportados (simplificado)
            var requestUrl = $"language/translate/v2/languages?key={_options.ApiKey}&target=en";
            var response = await _httpClient.GetAsync(requestUrl);

            return response.IsSuccessStatusCode; // Simplificado para este ejemplo
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar soporte de idiomas en Google");
            return false;
        }
    }
}

// ===== CLASES PARA DESERIALIZACIÓN =====

public class GoogleTranslationResponse
{
    public GoogleTranslationData? Data { get; set; }
}

public class GoogleTranslationData
{
    public GoogleTranslation[]? Translations { get; set; }
}

public class GoogleTranslation
{
    public string? TranslatedText { get; set; }
    public string? DetectedSourceLanguage { get; set; }
}