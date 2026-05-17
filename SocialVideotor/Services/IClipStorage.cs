namespace SocialVideotor.Services;

public interface IClipStorage
{
    Task SaveAsync(Stream content, string blobPath, CancellationToken cancellationToken = default);
    string GetAbsolutePath(string blobPath);
    string GetPublicPath(string blobPath);
    void DeletePrefix(string prefix);
}
