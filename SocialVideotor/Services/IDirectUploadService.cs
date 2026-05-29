namespace SocialVideotor.Services;

public interface IDirectUploadService
{
    bool IsConfigured { get; }
    Task<(string BlobName, string UploadUrl, DateTime ExpiresAtUtc)> CreateUploadUrlAsync(string userId, string jobId, CancellationToken cancellationToken = default);
    Task<bool> BlobExistsAsync(string blobName, CancellationToken cancellationToken = default);
    Task<DirectUploadBlobProperties?> GetBlobPropertiesAsync(string blobName, CancellationToken cancellationToken = default);
    Task DownloadBlobAsync(string blobName, string destinationPath, CancellationToken cancellationToken = default);
    Task DeleteBlobIfExistsAsync(string blobName, CancellationToken cancellationToken = default);
}

public sealed class DirectUploadBlobProperties
{
    public long ContentLength { get; init; }
    public string ContentType { get; init; } = string.Empty;
}
