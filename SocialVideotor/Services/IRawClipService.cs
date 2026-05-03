using SocialVideotor.Models;

namespace SocialVideotor.Services;

public interface IRawClipService
{
    Task<RawClipJob> StartProcessingAsync(Stream videoStream, string filename, long fileSize, CancellationToken cancellationToken = default);
    RawClipJob? GetJob(string jobId);
    IEnumerable<RawClipJob> GetAllJobs();
    /// <summary>Removes the job from memory and deletes all associated files on disk.</summary>
    void DeleteJob(string jobId);
}
