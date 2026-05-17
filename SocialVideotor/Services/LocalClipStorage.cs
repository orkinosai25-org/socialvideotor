using Microsoft.Extensions.Options;
namespace SocialVideotor.Services;

public class LocalClipStorage : IClipStorage
{
    private readonly IWebHostEnvironment _env;
    private readonly IOptions<VideoProcessingOptions> _options;

    public LocalClipStorage(IWebHostEnvironment env, IOptions<VideoProcessingOptions> options)
    {
        _env = env;
        _options = options;
    }

    private string RootPath => Path.GetFullPath(Path.Combine(_env.ContentRootPath, _options.Value.StorageRootPath));

    public async Task SaveAsync(Stream content, string blobPath, CancellationToken cancellationToken = default)
    {
        var absolutePath = GetAbsolutePath(blobPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await using var file = File.Create(absolutePath);
        await content.CopyToAsync(file, cancellationToken);
    }

    public string GetAbsolutePath(string blobPath)
    {
        var candidate = Path.GetFullPath(Path.Combine(RootPath, blobPath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSep = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected or invalid blob path format.");
        return candidate;
    }

    public string GetPublicPath(string blobPath)
    {
        var normalized = blobPath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !string.Equals(segments[0], "jobs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Blob path must follow jobs/{jobId}/{filename}.");
        return $"/api/clips/{segments[1]}/{segments[^1]}";
    }

    public void DeletePrefix(string prefix)
    {
        var root = GetAbsolutePath(prefix);
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
