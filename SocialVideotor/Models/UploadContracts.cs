namespace SocialVideotor.Models;

public class UploadInitiateRequest
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? ContentType { get; set; }
}

public class UploadInitiateResponse
{
    public string JobId { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public class UploadCapabilitiesResponse
{
    public bool DirectUploadEnabled { get; set; }
}

public class UploadCompleteRequest
{
    public string JobId { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
}
