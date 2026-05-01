using SocialVideotor.Models;

namespace SocialVideotor.Services;

public interface IVideoAIService
{
    Task<List<GenerationSuggestion>> GenerateSuggestionsAsync(string transcript, List<SocialPlatform> platforms, CancellationToken cancellationToken = default);
    Task<List<string>> GenerateHashtagsAsync(string content, SocialPlatform platform, CancellationToken cancellationToken = default);
    Task<string> GenerateCaptionAsync(string clipDescription, SocialPlatform platform, CancellationToken cancellationToken = default);
}
