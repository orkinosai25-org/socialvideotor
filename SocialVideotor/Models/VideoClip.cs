namespace SocialVideotor.Models;

public class VideoClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public double StartTime { get; set; }
    public double EndTime { get; set; }
    public string ThumbnailUrl { get; set; } = string.Empty;
    public List<TextTile> TextTiles { get; set; } = new();
    public List<GenerationSuggestion> Suggestions { get; set; } = new();
    public bool IsSelected { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Duration => EndTime - StartTime;
}
