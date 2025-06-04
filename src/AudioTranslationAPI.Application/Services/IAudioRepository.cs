using AudioTranslationAPI.Domain.Entities;

namespace AudioTranslationAPI.Application.Services
{
    public interface IAudioRepository
    {
        Task<AudioTranslation> CreateAsync(AudioTranslation translation);
        Task<AudioTranslation?> GetByIdAsync(Guid id);
        Task<AudioTranslation> UpdateAsync(AudioTranslation translation);
        Task DeleteAsync(Guid id);
        Task<IEnumerable<AudioTranslation>> GetByUserIdAsync(string userId);
        Task<IEnumerable<AudioTranslation>> GetExpiredAsync();
    }
}
