using SocialVideotor.Models;

namespace SocialVideotor.Services;

public interface IRawClipService
{
    Task<RawClipJob> StartProcessingAsync(Stream videoStream, string filename, long fileSize, string? contentType = null, string userId = "anonymous", CancellationToken cancellationToken = default);
    Task<RawClipJob> InitiateDirectUploadAsync(string jobId, string filename, long fileSize, string? contentType, string sourceIngressBlobName, string userId = "anonymous", CancellationToken cancellationToken = default);
    Task<RawClipJob?> CompleteDirectUploadAsync(string jobId, string? userId = null, CancellationToken cancellationToken = default);
    RawClipJob? GetJob(string jobId);
    IEnumerable<RawClipJob> GetAllJobs(string? userId = null);
    IEnumerable<RawClip> GetClips(string jobId, string? userId = null);
    RawClipJobStatus? GetJobStatus(string jobId, string? userId = null);
    Task<bool> RetryJobAsync(string jobId, string? userId = null, CancellationToken cancellationToken = default);
    Task ProcessQueuedJobsAsync(CancellationToken cancellationToken);
    void DeleteJob(string jobId);
}
