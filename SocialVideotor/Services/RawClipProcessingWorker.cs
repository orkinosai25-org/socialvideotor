namespace SocialVideotor.Services;

public class RawClipProcessingWorker : BackgroundService
{
    private readonly IRawClipService _rawClipService;
    private readonly ILogger<RawClipProcessingWorker> _logger;

    public RawClipProcessingWorker(IRawClipService rawClipService, ILogger<RawClipProcessingWorker> logger)
    {
        _rawClipService = rawClipService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Raw clip processing worker started.");
        try
        {
            await _rawClipService.ProcessQueuedJobsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Raw clip processing worker stopping.");
        }
    }
}
