using AudioTranslationAPI.Application.DTOs;
using AudioTranslationAPI.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AudioTranslationAPI.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public class AudioTranslationController : ControllerBase
{
    private readonly IAudioTranslationService _audioTranslationService;
    private readonly ILogger<AudioTranslationController> _logger;

    public AudioTranslationController(
        IAudioTranslationService audioTranslationService,
        ILogger<AudioTranslationController> logger)
    {
        _audioTranslationService = audioTranslationService;
        _logger = logger;
    }

    /// <summary>
    /// Inicia el proceso de traducción de audio de forma asíncrona
    /// </summary>
    /// <param name="request">Datos del audio y configuración de traducción</param>
    /// <returns>ID de traducción para seguimiento</returns>
    [HttpPost]
    [ProducesResponseType(typeof(TranslationResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10MB limit
    public async Task<IActionResult> StartTranslation([FromForm] ApiTranslationRequestDto request)
    {
        try
        {
            // Validaciones de archivo
            if (request.AudioFile == null || request.AudioFile.Length == 0)
            {
                return BadRequest(new { error = "Archivo de audio requerido" });
            }

            if (request.AudioFile.Length > 10 * 1024 * 1024) // 10MB
            {
                return StatusCode(413, new { error = "El archivo es demasiado grande. Máximo 10MB" });
            }

            var fileExtension = Path.GetExtension(request.AudioFile.FileName).ToLower();
            var allowedExtensions = new[] { ".wav", ".mp3", ".ogg", ".m4a" };

            _logger.LogError("DEBUG - FileName: {FileName}, Extension: '{Extension}'", request.AudioFile.FileName, fileExtension);

            if (!allowedExtensions.Contains(fileExtension))
            {
                return BadRequest(new { error = $"Formato de archivo no soportado: {fileExtension}" });
            }

            _logger.LogInformation("CONTROLLER: Pasó validación de extensión OK");


            // Ignorar ContentType si es octet-stream y confiar en la extensión
            _logger.LogInformation("Archivo: {FileName}, ContentType: {ContentType}", request.AudioFile.FileName, request.AudioFile.ContentType);

            // Convertir IFormFile a bytes
            byte[] audioData;
            using (var memoryStream = new MemoryStream())
            {
                await request.AudioFile.CopyToAsync(memoryStream);
                audioData = memoryStream.ToArray();
            }

            // Mapear a DTO de Application
            var translationRequest = new TranslationRequestDto
            {
                AudioData = audioData,
                FileName = request.AudioFile.FileName,
                ContentType = request.AudioFile.ContentType,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                UserId = request.UserId,
                FileSizeBytes = request.AudioFile.Length
            };

            var result = await _audioTranslationService.StartTranslationAsync(translationRequest);

            return Accepted(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning("Solicitud inválida: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning("Operación inválida: {Message}", ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inesperado al iniciar traducción");
            return StatusCode(500, new { error = "Error interno del servidor" });
        }
    }

    /// <summary>
    /// Consulta el estado de una traducción en proceso
    /// </summary>
    /// <param name="translationId">ID de la traducción</param>
    /// <returns>Estado actual de la traducción</returns>
    [HttpGet("{translationId}/status")]
    [ProducesResponseType(typeof(TranslationStatusDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTranslationStatus(Guid translationId)
    {
        try
        {
            var status = await _audioTranslationService.GetTranslationStatusAsync(translationId);

            if (status == null)
            {
                return NotFound(new { error = "Traducción no encontrada" });
            }

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar estado de traducción {TranslationId}", translationId);
            return StatusCode(500, new { error = "Error interno del servidor" });
        }
    }

    /// <summary>
    /// Descarga el audio traducido una vez completado el proceso
    /// </summary>
    /// <param name="translationId">ID de la traducción</param>
    /// <returns>Archivo de audio traducido</returns>
    [HttpGet("{translationId}/download")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DownloadTranslatedAudio(Guid translationId)
    {
        try
        {
            var audioResult = await _audioTranslationService.GetTranslatedAudioAsync(translationId);

            if (audioResult == null)
            {
                return NotFound(new { error = "Traducción no encontrada" });
            }

            if (!audioResult.IsCompleted)
            {
                return Conflict(new
                {
                    error = "La traducción aún no está completada",
                    status = audioResult.Status,
                    progress = audioResult.Progress
                });
            }

            return File(audioResult.AudioData, audioResult.ContentType, audioResult.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al descargar audio traducido {TranslationId}", translationId);
            return StatusCode(500, new { error = "Error interno del servidor" });
        }
    }
}
