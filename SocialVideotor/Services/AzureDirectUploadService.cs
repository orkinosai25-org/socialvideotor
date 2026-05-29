using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace SocialVideotor.Services;

public class AzureDirectUploadService : IDirectUploadService
{
    private readonly DirectUploadOptions _options;
    private readonly BlobContainerClient? _containerClient;

    public AzureDirectUploadService(IOptions<DirectUploadOptions> options)
    {
        _options = options.Value;

        if (!string.IsNullOrWhiteSpace(_options.AzureBlobConnectionString))
        {
            _containerClient = new BlobContainerClient(_options.AzureBlobConnectionString, _options.RawUploadsContainer);
        }
    }

    public bool IsConfigured => _containerClient != null;

    public async Task<(string BlobName, string UploadUrl, DateTime ExpiresAtUtc)> CreateUploadUrlAsync(string userId, string jobId, CancellationToken cancellationToken = default)
    {
        var container = GetRequiredContainerClient();
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var safeUserId = Regex.Replace(string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId, "[^a-zA-Z0-9-]", "-");
        var blobName = $"{safeUserId}/{jobId}/source.mp4";
        var blobClient = container.GetBlobClient(blobName);

        var sasExpirationMinutes = Math.Clamp(_options.SasExpirationMinutes, 1, 60);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(sasExpirationMinutes);

        if (!blobClient.CanGenerateSasUri)
            throw new InvalidOperationException("Azure Blob SAS generation is not available with current storage credentials.");

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = container.Name,
            BlobName = blobName,
            Resource = "b",
            ExpiresOn = expiresAt
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Create | BlobSasPermissions.Write);

        var uploadUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
        return (blobName, uploadUrl, expiresAt.UtcDateTime);
    }

    public async Task<bool> BlobExistsAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blobClient = GetRequiredContainerClient().GetBlobClient(blobName);
        var exists = await blobClient.ExistsAsync(cancellationToken);
        return exists.Value;
    }

    public async Task<DirectUploadBlobProperties?> GetBlobPropertiesAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blobClient = GetRequiredContainerClient().GetBlobClient(blobName);
        try
        {
            var response = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);
            return new DirectUploadBlobProperties
            {
                ContentLength = response.Value.ContentLength,
                ContentType = response.Value.ContentType ?? string.Empty
            };
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task DownloadBlobAsync(string blobName, string destinationPath, CancellationToken cancellationToken = default)
    {
        var blobClient = GetRequiredContainerClient().GetBlobClient(blobName);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await blobClient.DownloadToAsync(destinationPath, cancellationToken);
    }

    public async Task DeleteBlobIfExistsAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var blobClient = GetRequiredContainerClient().GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    private BlobContainerClient GetRequiredContainerClient()
    {
        if (_containerClient == null)
            throw new InvalidOperationException("Direct Azure upload is not configured. Set DirectUpload:AzureBlobConnectionString.");
        return _containerClient;
    }
}
