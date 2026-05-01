using SocialVideotor.Models;

namespace SocialVideotor.Services;

public interface IRawClipService
{
    Task<RawClipJob> StartProcessingAsync(Stream videoStream, string filename, long fileSize, CancellationToken cancellationToken = default);
    RawClipJob? GetJob(string jobId);
    IEnumerable<RawClipJob> GetAllJobs();
}
