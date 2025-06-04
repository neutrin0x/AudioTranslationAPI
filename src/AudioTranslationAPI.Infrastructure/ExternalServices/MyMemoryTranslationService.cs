using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Application.Configuration;
using AudioTranslationAPI.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Web;
using System.Text.Json.Serialization;

namespace AudioTranslationAPI.Infrastructure.ExternalServices;

/// <summary>
/// Servicio para traducción de texto usando MyMemory API (100% GRATUITO)
/// API Documentation: https://mymemory.translated.net/doc/spec.php
/// Límites: 10,000 caracteres/día sin registro, 50,000 con email
/// </summary>
public class MyMemoryTranslationService : ITranslationService
{
    private readonly ILogger<MyMemoryTranslationService> _logger;
    private readonly MyMemoryTranslationOptions _options;
    private readonly HttpClient _httpClient;

    // Mapeo de códigos de idioma para MyMemory (formato ISO 639-1)
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
        { "nl", "nl" },    // Holandés
        { "sv", "sv" },    // Sueco
        { "da", "da" },    // Danés
        { "no", "no" },    // Noruego
        { "fi", "fi" },    // Finlandés
        { "pl", "pl" },    // Polaco
        { "tr", "tr" },    // Turco
        { "el", "el" },    // Griego
        { "he", "he" },    // Hebreo
        { "th", "th" },    // Tailandés
        { "vi", "vi" },    // Vietnamita
        { "uk", "uk" },    // Ucraniano
        { "cs", "cs" },    // Checo
        { "hu", "hu" },    // Húngaro
        { "ro", "ro" },    // Rumano
        { "bg", "bg" },    // Búlgaro
        { "hr", "hr" },    // Croata
        { "sk", "sk" },    // Eslovaco
        { "sl", "sl" },    // Esloveno
        { "et", "et" },    // Estonio
        { "lv", "lv" },    // Letón
        { "lt", "lt" },    // Lituano
        { "mt", "mt" },    // Maltés
        { "ga", "ga" },    // Irlandés
        { "cy", "cy" }     // Galés
    };

    // Pares de idiomas con mejor calidad en MyMemory
    private readonly HashSet<(string, string)> _supportedPairs = new()
    {
        ("es", "en"), ("en", "es"),  // Español ↔ Inglés (Excelente)
        ("es", "fr"), ("fr", "es"),  // Español ↔ Francés (Muy buena)
        ("es", "pt"), ("pt", "es"),  // Español ↔ Portugués (Excelente)
        ("es", "it"), ("it", "es"),  // Español ↔ Italiano (Muy buena)
        ("en", "fr"), ("fr", "en"),  // Inglés ↔ Francés (Excelente)
        ("en", "de"), ("de", "en"),  // Inglés ↔ Alemán (Excelente)
        ("en", "it"), ("it", "en"),  // Inglés ↔ Italiano (Muy buena)
        ("en", "pt"), ("pt", "en"),  // Inglés ↔ Portugués (Muy buena)
        ("en", "ru"), ("ru", "en"),  // Inglés ↔ Ruso (Buena)
        ("en", "zh"), ("zh", "en"),  // Inglés ↔ Chino (Buena)
        ("en", "ja"), ("ja", "en"),  // Inglés ↔ Japonés (Buena)
        ("en", "ko"), ("ko", "en"),  // Inglés ↔ Coreano (Buena)
        ("en", "ar"), ("ar", "en"),  // Inglés ↔ Árabe (Buena)
        ("fr", "de"), ("de", "fr"),  // Francés ↔ Alemán (Muy buena)
        ("fr", "it"), ("it", "fr"),  // Francés ↔ Italiano (Muy buena)
        ("de", "it"), ("it", "de")   // Alemán ↔ Italiano (Buena)
    };

    public MyMemoryTranslationService(
        ILogger<MyMemoryTranslationService> logger,
        IOptions<ExternalServicesOptions> options,
        HttpClient httpClient)
    {
        _logger = logger;
        _options = options.Value.MyMemory;
        _httpClient = httpClient;

        // Configurar HttpClient para MyMemory
        _httpClient.BaseAddress = new Uri("https://api.mymemory.translated.net/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "AudioTranslationAPI/1.0");
        _httpClient.Timeout = TimeSpan.FromSeconds(30); // MyMemory puede ser un poco lento
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
            if (string.IsNullOrWhiteSpace(text))
            {
                return new TranslationResult
                {
                    TranslatedText = "",
                    Confidence = 1.0f,
                    DetectedSourceLanguage = fromLanguage
                };
            }

            _logger.LogDebug("Iniciando traducción con MyMemory: {TextLength} caracteres, {FromLang} → {ToLang}",
                text.Length, fromLanguage, toLanguage);

            // Validar longitud del texto (MyMemory tiene límite de ~500 caracteres por request para mejor calidad)
            if (text.Length > 500)
            {
                _logger.LogInformation("Texto largo detectado ({Length} caracteres), dividiendo en chunks", text.Length);
                return await TranslateLongTextAsync(text, fromLanguage, toLanguage);
            }

            var myMemoryFromLang = GetMyMemoryLanguageCode(fromLanguage);
            var myMemoryToLang = GetMyMemoryLanguageCode(toLanguage);
            var languagePair = $"{myMemoryFromLang}|{myMemoryToLang}";

            // Construir URL para MyMemory API
            var queryParams = new Dictionary<string, string>
            {
                { "q", text },
                { "langpair", languagePair }
            };

            // Agregar email si está configurado (aumenta el límite diario)
            if (!string.IsNullOrEmpty(_options.Email))
            {
                queryParams.Add("de", _options.Email);
            }

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}"));
            var requestUrl = $"get?{queryString}";

            _logger.LogDebug("Solicitando traducción a MyMemory: {LanguagePair}", languagePair);

            // Realizar petición a MyMemory API
            var response = await _httpClient.GetAsync(requestUrl);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Error en MyMemory API: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
                throw new InvalidOperationException($"Error en MyMemory API: {response.StatusCode}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var myMemoryResponse = JsonSerializer.Deserialize<MyMemoryResponse>(responseContent,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            if (myMemoryResponse?.ResponseData == null)
            {
                throw new InvalidOperationException("MyMemory no devolvió una respuesta válida");
            }

            var translatedText = myMemoryResponse.ResponseData.TranslatedText ?? "";

            // MyMemory a veces devuelve el texto original si no puede traducir
            if (translatedText.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("MyMemory devolvió el texto original, posible error de traducción");
            }

            var confidence = CalculateConfidence(myMemoryResponse);

            var result = new TranslationResult
            {
                TranslatedText = translatedText,
                Confidence = confidence,
                DetectedSourceLanguage = fromLanguage // MyMemory no devuelve idioma detectado
            };

            _logger.LogInformation("Traducción completada con MyMemory: {OriginalLength} → {TranslatedLength} caracteres, Confianza: {Confidence:P}, Match: {Match}%",
                text.Length, result.TranslatedText.Length, result.Confidence, myMemoryResponse.ResponseData.Match);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la traducción con MyMemory");
            throw new InvalidOperationException($"Error en traducción: {ex.Message}", ex);
        }
    }

    public async Task<bool> IsLanguagePairSupportedAsync(string fromLanguage, string toLanguage)
    {
        try
        {
            var myMemoryFromLang = GetMyMemoryLanguageCode(fromLanguage);
            var myMemoryToLang = GetMyMemoryLanguageCode(toLanguage);

            // Verificar en nuestro mapeo local
            return _supportedPairs.Contains((myMemoryFromLang, myMemoryToLang));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar soporte de idiomas {FromLang} → {ToLang}", fromLanguage, toLanguage);
            return false;
        }
    }

    // ===== MÉTODOS AUXILIARES =====

    private string GetMyMemoryLanguageCode(string languageCode)
    {
        var normalizedCode = languageCode.ToLowerInvariant();

        if (_languageMapping.TryGetValue(normalizedCode, out var myMemoryCode))
        {
            return myMemoryCode;
        }

        // Fallback: usar el código tal como viene (MyMemory es flexible)
        _logger.LogWarning("Código de idioma no mapeado: {LanguageCode}, usando como está", languageCode);
        return languageCode;
    }

    private float CalculateConfidence(MyMemoryResponse response)
    {
        var baseConfidence = 0.7f; // Confianza base para MyMemory

        // Ajustar basado en el match score de MyMemory
        if (response.ResponseData != null)
        {
            var matchScore = response.ResponseData.Match;

            if (matchScore >= 95)
                baseConfidence = 0.95f;
            else if (matchScore >= 90)
                baseConfidence = 0.90f;
            else if (matchScore >= 80)
                baseConfidence = 0.85f;
            else if (matchScore >= 70)
                baseConfidence = 0.80f;
            else if (matchScore >= 50)
                baseConfidence = 0.75f;
            else
                baseConfidence = 0.65f;
        }

        // Verificar si hay múltiples matches (indica mejor calidad)
        //if (response.Matches != null && response.Matches.Length > 1)
        //{
        //    baseConfidence += 0.05f;
        //}

        return Math.Min(baseConfidence, 1.0f);
    }

    private async Task<TranslationResult> TranslateLongTextAsync(string text, string fromLanguage, string toLanguage)
    {
        const int chunkSize = 400; // Chunks más pequeños para MyMemory
        var chunks = SplitTextIntoChunks(text, chunkSize);
        var translatedChunks = new List<string>();
        var totalConfidence = 0.0f;

        _logger.LogInformation("Traduciendo texto largo en {ChunkCount} chunks con MyMemory", chunks.Count);

        foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
        {
            try
            {
                var chunkResult = await TranslateWithConfidenceAsync(chunk, fromLanguage, toLanguage);
                translatedChunks.Add(chunkResult.TranslatedText);
                totalConfidence += chunkResult.Confidence;

                // Pausa más larga para MyMemory (API gratuita)
                if (index < chunks.Count - 1) // No pausa en el último chunk
                {
                    await Task.Delay(1000); // 1 segundo entre requests
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al traducir chunk {Index}/{Total} con MyMemory", index + 1, chunks.Count);
                throw;
            }
        }

        return new TranslationResult
        {
            TranslatedText = string.Join(" ", translatedChunks),
            Confidence = totalConfidence / chunks.Count,
            DetectedSourceLanguage = fromLanguage
        };
    }

    private List<string> SplitTextIntoChunks(string text, int maxChunkSize)
    {
        var chunks = new List<string>();
        var sentences = text.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var currentChunk = new List<string>();
        var currentLength = 0;

        foreach (var sentence in sentences)
        {
            var trimmedSentence = sentence.Trim();
            if (string.IsNullOrEmpty(trimmedSentence)) continue;

            if (currentLength + trimmedSentence.Length > maxChunkSize && currentChunk.Any())
            {
                chunks.Add(string.Join(". ", currentChunk) + ".");
                currentChunk.Clear();
                currentLength = 0;
            }

            currentChunk.Add(trimmedSentence);
            currentLength += trimmedSentence.Length + 2; // +2 para ". "
        }

        if (currentChunk.Any())
        {
            chunks.Add(string.Join(". ", currentChunk) + ".");
        }

        return chunks;
    }
}

// ===== CLASES PARA DESERIALIZACIÓN =====

public class MyMemoryResponse
{
    [JsonPropertyName("responseData")]
    public MyMemoryResponseData? ResponseData { get; set; }

    [JsonPropertyName("quotaFinished")]
    public object? QuotaFinished { get; set; }

    [JsonPropertyName("matches")]
    public object[]? Matches { get; set; }

    [JsonPropertyName("responseDetails")]
    public string? ResponseDetails { get; set; }

    [JsonPropertyName("responseStatus")]
    public int ResponseStatus { get; set; }
}

public class MyMemoryResponseData
{
    [JsonPropertyName("translatedText")]
    public string? TranslatedText { get; set; }

    [JsonPropertyName("match")]
    public double Match { get; set; }
}

