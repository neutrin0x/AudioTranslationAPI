using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Servicio de background para procesamiento asíncrono
    /// </summary>
    public interface IAudioTranslationBackgroundService
    {
        Task ProcessTranslationAsync(Guid translationId);
        Task RetryFailedTranslationAsync(Guid translationId);
        Task CleanupExpiredTranslationsAsync();
    }
}
