using SocialVideotor.Models;

namespace SocialVideotor.Services;

public interface IProjectService
{
    IEnumerable<VideoProject> GetAllProjects();
    VideoProject? GetProject(string id);
    VideoProject CreateProject(string name, string videoUrl);
    void UpdateProject(VideoProject project);
    void DeleteProject(string id);
    void AddClipToProject(string projectId, VideoClip clip);
    void UpdateClip(string projectId, VideoClip clip);
    void RemoveClip(string projectId, string clipId);
}
