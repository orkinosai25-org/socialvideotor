namespace SocialVideotor.Models;

public class TextTile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Text { get; set; } = string.Empty;
    public string Position { get; set; } = "bottom";
    public int FontSize { get; set; } = 24;
    public string Color { get; set; } = "#FFFFFF";
    public string BackgroundColor { get; set; } = "#000000";
    public double StartTime { get; set; } = 0;
    public double EndTime { get; set; } = 5;
}
