namespace SocialVideotor.Services;

public static class UserIdentityResolver
{
    private const string AnonymousUserCookieName = "svt_uid";
    public const string AnonymousFallbackUserId = "anonymous";

    public static readonly HashSet<string> SupportedUploadContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4",
        "video/quicktime",
        "video/x-m4v"
    };

    public static string ResolveForRequest(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            return context.User.Identity.Name ?? "authenticated-user";

        if (context.Request.Cookies.TryGetValue(AnonymousUserCookieName, out var cookieValue)
            && Guid.TryParse(cookieValue, out var existingId))
        {
            return $"anon-{existingId:D}";
        }

        var generatedId = Guid.NewGuid();
        context.Response.Cookies.Append(
            AnonymousUserCookieName,
            generatedId.ToString("D"),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                MaxAge = TimeSpan.FromDays(365)
            });

        return $"anon-{generatedId:D}";
    }

    public static string? ResolveFromContextWithoutMutation(HttpContext? context)
    {
        if (context == null)
            return null;

        if (context.User.Identity?.IsAuthenticated == true)
            return context.User.Identity.Name ?? "authenticated-user";

        if (context.Request.Cookies.TryGetValue(AnonymousUserCookieName, out var cookieValue)
            && Guid.TryParse(cookieValue, out var existingId))
        {
            return $"anon-{existingId:D}";
        }

        return null;
    }

    public static string ResolveFromContextOrFallback(HttpContext? context)
    {
        var userId = ResolveFromContextWithoutMutation(context);
        return string.IsNullOrWhiteSpace(userId) ? AnonymousFallbackUserId : userId;
    }
}
