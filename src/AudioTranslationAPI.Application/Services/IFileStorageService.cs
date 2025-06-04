using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioTranslationAPI.Application.Services
{
    /// <summary>
    /// Almacenamiento de archivos
    /// </summary>
    public interface IFileStorageService
    {
        Task<string> SaveAudioAsync(byte[] audioData, string fileName, string directory);
        Task<byte[]> LoadAudioAsync(string filePath);
        Task DeleteFileAsync(string filePath);
        Task<bool> FileExistsAsync(string filePath);
        Task CleanupExpiredFilesAsync(TimeSpan maxAge);
    }
}
