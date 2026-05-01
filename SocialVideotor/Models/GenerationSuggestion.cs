namespace SocialVideotor.Models;

public class GenerationSuggestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public List<string> Hashtags { get; set; } = new();
    public List<SocialPlatform> RecommendedPlatforms { get; set; } = new();
    public double EngagementScore { get; set; }
    public string SuggestedCaption { get; set; } = string.Empty;
}
