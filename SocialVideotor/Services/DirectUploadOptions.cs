namespace SocialVideotor.Services;

public class DirectUploadOptions
{
    public string AzureBlobConnectionString { get; set; } = string.Empty;
    public string RawUploadsContainer { get; set; } = "raw-uploads";
    public int SasExpirationMinutes { get; set; } = 15;
}
