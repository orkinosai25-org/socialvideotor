using SocialVideotor.Models;

namespace SocialVideotor.Services;

public class ProjectService : IProjectService
{
    private readonly List<VideoProject> _projects = new();

    public ProjectService()
    {
        var demoProject = new VideoProject
        {
            Id = "demo-1",
            Name = "My First Social Video",
            VideoUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            Status = ProjectStatus.Ready,
            TargetPlatforms = new List<SocialPlatform> { SocialPlatform.Instagram, SocialPlatform.TikTok },
            VideoDuration = 213,
            Transcript = "This is a demo transcript for the video content.",
            Clips = new List<VideoClip>
            {
                new VideoClip
                {
                    Id = "clip-1",
                    Title = "Intro Hook",
                    StartTime = 0,
                    EndTime = 15,
                    IsSelected = true,
                    Description = "Great opening hook for social media",
                    TextTiles = new List<TextTile>
                    {
                        new TextTile { Text = "Check this out!", Position = "top", FontSize = 28, Color = "#FFFFFF" }
                    }
                },
                new VideoClip
                {
                    Id = "clip-2",
                    Title = "Key Moment",
                    StartTime = 45,
                    EndTime = 75,
                    Description = "High engagement segment",
                    TextTiles = new List<TextTile>()
                }
            }
        };
        _projects.Add(demoProject);
    }

    public IEnumerable<VideoProject> GetAllProjects() => _projects.AsReadOnly();

    public VideoProject? GetProject(string id) => _projects.FirstOrDefault(p => p.Id == id);

    public VideoProject CreateProject(string name, string videoUrl)
    {
        var project = new VideoProject
        {
            Name = name,
            VideoUrl = videoUrl,
            Status = ProjectStatus.Draft
        };
        _projects.Add(project);
        return project;
    }

    public void UpdateProject(VideoProject project)
    {
        var index = _projects.FindIndex(p => p.Id == project.Id);
        if (index >= 0)
        {
            project.UpdatedAt = DateTime.UtcNow;
            _projects[index] = project;
        }
    }

    public void DeleteProject(string id)
    {
        var project = _projects.FirstOrDefault(p => p.Id == id);
        if (project != null) _projects.Remove(project);
    }

    public void AddClipToProject(string projectId, VideoClip clip)
    {
        var project = GetProject(projectId);
        project?.Clips.Add(clip);
    }

    public void UpdateClip(string projectId, VideoClip clip)
    {
        var project = GetProject(projectId);
        if (project == null) return;
        var index = project.Clips.FindIndex(c => c.Id == clip.Id);
        if (index >= 0) project.Clips[index] = clip;
    }

    public void RemoveClip(string projectId, string clipId)
    {
        var project = GetProject(projectId);
        var clip = project?.Clips.FirstOrDefault(c => c.Id == clipId);
        if (clip != null) project!.Clips.Remove(clip);
    }
}
