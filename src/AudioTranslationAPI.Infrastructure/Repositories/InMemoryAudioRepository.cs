using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Domain.Entities;
using AudioTranslationAPI.Domain.Types;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AudioTranslationAPI.Infrastructure.Repositories;

/// <summary>
/// Repositorio en memoria para almacenar traducciones de audio durante el desarrollo
/// Thread-safe para manejar operaciones concurrentes
/// </summary>
public class InMemoryAudioRepository : IAudioRepository
{
    private readonly ILogger<InMemoryAudioRepository> _logger;
    private readonly ConcurrentDictionary<Guid, AudioTranslation> _translations;
    private readonly ConcurrentDictionary<string, HashSet<Guid>> _userTranslations;
    private readonly object _lockObject = new();

    public InMemoryAudioRepository(ILogger<InMemoryAudioRepository> logger)
    {
        _logger = logger;
        _translations = new ConcurrentDictionary<Guid, AudioTranslation>();
        _userTranslations = new ConcurrentDictionary<string, HashSet<Guid>>();

        _logger.LogInformation("InMemoryAudioRepository inicializado");
    }

    public async Task<AudioTranslation> CreateAsync(AudioTranslation translation)
    {
        try
        {
            Console.WriteLine($"=== CREAR: Intentando guardar ID: {translation.Id} ===");

            if (translation == null)
                throw new ArgumentNullException(nameof(translation));

            var added = _translations.TryAdd(translation.Id, translation);

            Console.WriteLine($"=== CREAR: TryAdd resultado: {added} ===");
            Console.WriteLine($"=== CREAR: Total items después: {_translations.Count} ===");

            if (!added)
            {
                throw new InvalidOperationException($"Ya existe una traducción con ID {translation.Id}");
            }

            Console.WriteLine($"=== CREAR: SUCCESS ===");
            return await Task.FromResult(translation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== CREAR ERROR: {ex.Message} ===");
            throw;
        }
    }

    public async Task<AudioTranslation?> GetByIdAsync(Guid id)
    {
        try
        {
            Console.WriteLine($"=== REPOSITORIO: Buscando ID: {id} ===");
            Console.WriteLine($"=== REPOSITORIO: Total items en diccionario: {_translations.Count} ===");

            foreach (var kvp in _translations)
            {
                Console.WriteLine($"=== REPOSITORIO: Item en diccionario: {kvp.Key} ===");
            }

            _translations.TryGetValue(id, out var translation);

            Console.WriteLine($"=== REPOSITORIO: Resultado: {(translation != null ? "ENCONTRADO" : "NULL")} ===");

            return await Task.FromResult(translation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"=== REPOSITORIO ERROR: {ex.Message} ===");
            throw;
        }
    }

    public async Task<AudioTranslation> UpdateAsync(AudioTranslation translation)
    {
        try
        {
            if (translation == null)
                throw new ArgumentNullException(nameof(translation));

            // Actualizar en el diccionario principal
            _translations.AddOrUpdate(
                translation.Id,
                translation,
                (key, oldValue) => translation);

            _logger.LogDebug("Traducción actualizada: {TranslationId}, Estado: {Status}, Progreso: {Progress}%",
                translation.Id, translation.Status, translation.Progress);

            return await Task.FromResult(translation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al actualizar traducción {TranslationId}", translation?.Id);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            // Obtener la traducción antes de eliminarla para limpiar índices
            if (_translations.TryGetValue(id, out var translation))
            {
                // Eliminar del diccionario principal
                _translations.TryRemove(id, out _);

                // Limpiar del índice por usuario
                if (!string.IsNullOrEmpty(translation.UserId))
                {
                    lock (_lockObject)
                    {
                        if (_userTranslations.TryGetValue(translation.UserId, out var userTranslations))
                        {
                            userTranslations.Remove(id);

                            // Si no quedan traducciones para este usuario, eliminar la entrada
                            if (!userTranslations.Any())
                            {
                                _userTranslations.TryRemove(translation.UserId, out _);
                            }
                        }
                    }
                }

                _logger.LogInformation("Traducción eliminada: {TranslationId}", id);
            }
            else
            {
                _logger.LogWarning("Intento de eliminar traducción inexistente: {TranslationId}", id);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar traducción {TranslationId}", id);
            throw;
        }
    }

    public async Task<IEnumerable<AudioTranslation>> GetByUserIdAsync(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return await Task.FromResult(Enumerable.Empty<AudioTranslation>());
            }

            var userTranslationIds = new HashSet<Guid>();

            lock (_lockObject)
            {
                if (_userTranslations.TryGetValue(userId, out var ids))
                {
                    userTranslationIds = new HashSet<Guid>(ids);
                }
            }

            var userTranslations = new List<AudioTranslation>();

            foreach (var translationId in userTranslationIds)
            {
                if (_translations.TryGetValue(translationId, out var translation))
                {
                    userTranslations.Add(translation);
                }
            }

            // Ordenar por fecha de creación (más recientes primero)
            var sortedTranslations = userTranslations
                .OrderByDescending(t => t.CreatedAt)
                .ToList();

            _logger.LogDebug("Encontradas {Count} traducciones para usuario {UserId}",
                sortedTranslations.Count, userId);

            return await Task.FromResult(sortedTranslations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener traducciones del usuario {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<AudioTranslation>> GetExpiredAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiredTranslations = _translations.Values
                .Where(t => t.ExpiresAt.HasValue && t.ExpiresAt.Value < now)
                .Where(t => t.Status != TranslationStatus.Expired) // Solo las que no están marcadas como expiradas
                .ToList();

            _logger.LogDebug("Encontradas {Count} traducciones expiradas", expiredTranslations.Count);

            return await Task.FromResult(expiredTranslations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener traducciones expiradas");
            throw;
        }
    }

    // ===== MÉTODOS ADICIONALES ÚTILES =====

    /// <summary>
    /// Obtiene estadísticas del repositorio para debugging/monitoring
    /// </summary>
    public async Task<RepositoryStats> GetStatsAsync()
    {
        try
        {
            var stats = new RepositoryStats
            {
                TotalTranslations = _translations.Count,
                UniqueUsers = _userTranslations.Count,
                TranslationsByStatus = _translations.Values
                    .GroupBy(t => t.Status)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count()),
                OldestTranslation = _translations.Values.Any()
                    ? _translations.Values.Min(t => t.CreatedAt)
                    : null,
                NewestTranslation = _translations.Values.Any()
                    ? _translations.Values.Max(t => t.CreatedAt)
                    : null
            };

            return await Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener estadísticas del repositorio");
            throw;
        }
    }

    /// <summary>
    /// Limpia traducciones completadas o fallidas más antiguas que el tiempo especificado
    /// </summary>
    public async Task<int> CleanupOldTranslationsAsync(TimeSpan maxAge)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.Subtract(maxAge);
            var completedStatuses = new[] { TranslationStatus.Completed, TranslationStatus.Failed, TranslationStatus.Expired };

            var toDelete = _translations.Values
                .Where(t => completedStatuses.Contains(t.Status))
                .Where(t => t.CreatedAt < cutoffDate)
                .Select(t => t.Id)
                .ToList();

            var deletedCount = 0;
            foreach (var id in toDelete)
            {
                await DeleteAsync(id);
                deletedCount++;
            }

            _logger.LogInformation("Limpieza completada: {DeletedCount} traducciones antiguas eliminadas", deletedCount);
            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la limpieza de traducciones antiguas");
            throw;
        }
    }
}

/// <summary>
/// Estadísticas del repositorio para monitoring
/// </summary>
public class RepositoryStats
{
    public int TotalTranslations { get; set; }
    public int UniqueUsers { get; set; }
    public Dictionary<string, int> TranslationsByStatus { get; set; } = new();
    public DateTime? OldestTranslation { get; set; }
    public DateTime? NewestTranslation { get; set; }
}