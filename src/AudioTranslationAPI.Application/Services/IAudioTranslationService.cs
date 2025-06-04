using AudioTranslationAPI.Application.DTOs;

namespace AudioTranslationAPI.Application.Services
{
    public interface IAudioTranslationService
    {
        /// <summary>
        /// Inicia el proceso de traducción de audio de forma asíncrona
        /// </summary>
        /// <param name="request">Solicitud de traducción con archivo de audio</param>
        /// <returns>Respuesta con ID de traducción y estado inicial</returns>
        Task<TranslationResponseDto> StartTranslationAsync(TranslationRequestDto request);

        /// <summary>
        /// Obtiene el estado actual de una traducción
        /// </summary>
        /// <param name="translationId">ID único de la traducción</param>
        /// <returns>Estado detallado de la traducción o null si no existe</returns>
        Task<TranslationStatusDto?> GetTranslationStatusAsync(Guid translationId);

        /// <summary>
        /// Obtiene el archivo de audio traducido
        /// </summary>
        /// <param name="translationId">ID único de la traducción</param>
        /// <returns>Datos del audio traducido o null si no existe/no está listo</returns>
        Task<TranslatedAudioResultDto?> GetTranslatedAudioAsync(Guid translationId);

        /// <summary>
        /// Cancela una traducción en proceso
        /// </summary>
        /// <param name="translationId">ID único de la traducción</param>
        /// <returns>True si se canceló exitosamente</returns>
        Task<bool> CancelTranslationAsync(Guid translationId);
    }
}
