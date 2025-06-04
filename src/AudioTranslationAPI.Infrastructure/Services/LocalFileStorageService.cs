using AudioTranslationAPI.Application.Services;
using AudioTranslationAPI.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AudioTranslationAPI.Infrastructure.Services;

/// <summary>
/// Servicio para almacenamiento de archivos de audio en el sistema de archivos local
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly ILogger<LocalFileStorageService> _logger;
    private readonly AudioProcessingOptions _options;
    private readonly string _baseStoragePath;

    public LocalFileStorageService(
        ILogger<LocalFileStorageService> logger,
        IOptions<AudioProcessingOptions> options)
    {
        _logger = logger;
        _options = options.Value;

        // Configurar directorio base de almacenamiento
        _baseStoragePath = Path.Combine(Directory.GetCurrentDirectory(), "storage");

        // Crear directorios base si no existen
        InitializeStorageDirectories();
    }

    public async Task<string> SaveAudioAsync(byte[] audioData, string fileName, string directory)
    {
        try
        {
            if (audioData == null || audioData.Length == 0)
                throw new ArgumentException("Los datos de audio no pueden estar vacíos", nameof(audioData));

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("El nombre de archivo no puede estar vacío", nameof(fileName));

            // Validar y limpiar el directorio
            var sanitizedDirectory = SanitizeDirectoryName(directory);
            var targetDirectory = Path.Combine(_baseStoragePath, sanitizedDirectory);

            // Crear directorio si no existe
            Directory.CreateDirectory(targetDirectory);

            // Generar nombre de archivo único y seguro
            var safeFileName = GenerateSafeFileName(fileName);
            var fullPath = Path.Combine(targetDirectory, safeFileName);

            // Verificar que el archivo no exista (por seguridad)
            if (File.Exists(fullPath))
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(safeFileName);
                var extension = Path.GetExtension(safeFileName);
                safeFileName = $"{fileNameWithoutExt}_{timestamp}{extension}";
                fullPath = Path.Combine(targetDirectory, safeFileName);
            }

            _logger.LogDebug("Guardando archivo de audio: {FileName} ({Size} bytes) en {Directory}",
                safeFileName, audioData.Length, sanitizedDirectory);

            // Escribir archivo de forma asíncrona
            await File.WriteAllBytesAsync(fullPath, audioData);

            // Verificar que se escribió correctamente
            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists || fileInfo.Length != audioData.Length)
            {
                throw new InvalidOperationException("Error al verificar la escritura del archivo");
            }

            _logger.LogInformation("Archivo guardado exitosamente: {FilePath} ({Size} bytes)",
                fullPath, fileInfo.Length);

            return fullPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar archivo {FileName} en directorio {Directory}",
                fileName, directory);
            throw new InvalidOperationException($"Error al guardar archivo: {ex.Message}", ex);
        }
    }

    public async Task<byte[]> LoadAudioAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("La ruta del archivo no puede estar vacía", nameof(filePath));

            // Verificar que el archivo existe
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Archivo no encontrado: {filePath}");
            }

            // Verificar que está dentro de nuestro directorio de almacenamiento (seguridad)
            var fullPath = Path.GetFullPath(filePath);
            var basePath = Path.GetFullPath(_baseStoragePath);

            if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Acceso denegado: archivo fuera del directorio de almacenamiento");
            }

            _logger.LogDebug("Cargando archivo de audio: {FilePath}", filePath);

            // Leer archivo de forma asíncrona
            var audioData = await File.ReadAllBytesAsync(filePath);

            _logger.LogDebug("Archivo cargado exitosamente: {FilePath} ({Size} bytes)",
                filePath, audioData.Length);

            return audioData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al cargar archivo {FilePath}", filePath);
            throw new InvalidOperationException($"Error al cargar archivo: {ex.Message}", ex);
        }
    }

    public async Task DeleteFileAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                _logger.LogWarning("Intento de eliminar archivo con ruta vacía");
                return;
            }

            if (!File.Exists(filePath))
            {
                _logger.LogDebug("Archivo no existe, no se requiere eliminación: {FilePath}", filePath);
                return;
            }

            // Verificar que está dentro de nuestro directorio de almacenamiento (seguridad)
            var fullPath = Path.GetFullPath(filePath);
            var basePath = Path.GetFullPath(_baseStoragePath);

            if (!fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("Acceso denegado: archivo fuera del directorio de almacenamiento");
            }

            _logger.LogDebug("Eliminando archivo: {FilePath}", filePath);

            // Eliminar archivo
            File.Delete(filePath);

            // Intentar eliminar directorio si está vacío
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                        _logger.LogDebug("Directorio vacío eliminado: {Directory}", directory);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "No se pudo eliminar directorio vacío: {Directory}", directory);
                    // No es un error crítico, continuar
                }
            }

            _logger.LogInformation("Archivo eliminado exitosamente: {FilePath}", filePath);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al eliminar archivo {FilePath}", filePath);
            throw new InvalidOperationException($"Error al eliminar archivo: {ex.Message}", ex);
        }
    }

    public async Task<bool> FileExistsAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return false;

            var exists = File.Exists(filePath);

            _logger.LogDebug("Verificación de existencia de archivo: {FilePath} = {Exists}",
                filePath, exists);

            return await Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al verificar existencia del archivo {FilePath}", filePath);
            return false;
        }
    }

    public async Task CleanupExpiredFilesAsync(TimeSpan maxAge)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.Subtract(maxAge);
            var deletedCount = 0;

            _logger.LogInformation("Iniciando limpieza de archivos anteriores a {CutoffDate}", cutoffDate);

            deletedCount = await CleanupDirectoryAsync(_baseStoragePath, cutoffDate);

            _logger.LogInformation("Limpieza completada: {DeletedCount} archivos eliminados", deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la limpieza de archivos expirados");
            throw;
        }
    }

    // ===== MÉTODOS AUXILIARES PRIVADOS =====

    private void InitializeStorageDirectories()
    {
        try
        {
            // Crear directorio base
            Directory.CreateDirectory(_baseStoragePath);

            // Crear subdirectorios estándar
            var standardDirectories = new[] { "originals", "converted", "transcripts", "translated_audio", "temp" };

            foreach (var dir in standardDirectories)
            {
                var fullPath = Path.Combine(_baseStoragePath, dir);
                Directory.CreateDirectory(fullPath);
            }

            _logger.LogInformation("Directorios de almacenamiento inicializados en: {BasePath}", _baseStoragePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al inicializar directorios de almacenamiento");
            throw;
        }
    }

    private string SanitizeDirectoryName(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return "default";

        // Remover caracteres peligrosos
        var invalidChars = Path.GetInvalidPathChars().Concat(new[] { '.', '\\', '/' }).ToArray();
        var sanitized = new string(directory.Where(c => !invalidChars.Contains(c)).ToArray());

        // Prevenir path traversal
        sanitized = sanitized.Replace("..", "").Replace("~", "");

        if (string.IsNullOrWhiteSpace(sanitized))
            return "default";

        return sanitized;
    }

    private string GenerateSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "unnamed_file";

        // Obtener extensión
        var extension = Path.GetExtension(fileName);
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

        // Limpiar nombre
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(nameWithoutExt.Where(c => !invalidChars.Contains(c)).ToArray());

        // Truncar si es muy largo
        if (safeName.Length > 50)
            safeName = safeName.Substring(0, 50);

        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "file";

        // Generar hash único para evitar colisiones
        var uniqueId = GenerateShortHash(fileName + DateTime.UtcNow.Ticks);

        return $"{safeName}_{uniqueId}{extension}";
    }

    private string GenerateShortHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }

    private async Task<int> CleanupDirectoryAsync(string directoryPath, DateTime cutoffDate)
    {
        var deletedCount = 0;

        try
        {
            if (!Directory.Exists(directoryPath))
                return deletedCount;

            // Limpiar archivos
            var files = Directory.GetFiles(directoryPath);
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTime < cutoffDate)
                {
                    try
                    {
                        File.Delete(file);
                        deletedCount++;
                        _logger.LogDebug("Archivo expirado eliminado: {FilePath}", file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "No se pudo eliminar archivo expirado: {FilePath}", file);
                    }
                }
            }

            // Limpiar subdirectorios recursivamente
            var directories = Directory.GetDirectories(directoryPath);
            foreach (var directory in directories)
            {
                var subDirectoryDeleted = await CleanupDirectoryAsync(directory, cutoffDate);
                deletedCount += subDirectoryDeleted;

                // Eliminar directorio si está vacío
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                        _logger.LogDebug("Directorio vacío eliminado: {Directory}", directory);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "No se pudo eliminar directorio vacío: {Directory}", directory);
                }
            }

            return deletedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la limpieza del directorio {DirectoryPath}", directoryPath);
            return deletedCount;
        }
    }

    /// <summary>
    /// Obtiene estadísticas del almacenamiento
    /// </summary>
    public async Task<StorageStats> GetStorageStatsAsync()
    {
        try
        {
            var stats = new StorageStats();

            if (Directory.Exists(_baseStoragePath))
            {
                var allFiles = Directory.GetFiles(_baseStoragePath, "*", SearchOption.AllDirectories);

                stats.TotalFiles = allFiles.Length;
                stats.TotalSizeBytes = allFiles.Sum(f => new FileInfo(f).Length);
                stats.DirectoryCount = Directory.GetDirectories(_baseStoragePath, "*", SearchOption.AllDirectories).Length;

                if (allFiles.Any())
                {
                    stats.OldestFile = allFiles.Min(f => new FileInfo(f).CreationTime);
                    stats.NewestFile = allFiles.Max(f => new FileInfo(f).CreationTime);
                }
            }

            return await Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener estadísticas de almacenamiento");
            throw;
        }
    }
}

/// <summary>
/// Estadísticas del almacenamiento
/// </summary>
public class StorageStats
{
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public int DirectoryCount { get; set; }
    public DateTime? OldestFile { get; set; }
    public DateTime? NewestFile { get; set; }

    public string TotalSizeFormatted =>
        TotalSizeBytes < 1024 ? $"{TotalSizeBytes} B" :
        TotalSizeBytes < 1024 * 1024 ? $"{TotalSizeBytes / 1024:F1} KB" :
        TotalSizeBytes < 1024 * 1024 * 1024 ? $"{TotalSizeBytes / (1024 * 1024):F1} MB" :
        $"{TotalSizeBytes / (1024 * 1024 * 1024):F1} GB";
}