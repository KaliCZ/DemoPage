namespace Kalandra.Api.Infrastructure.Auth;

public class SupabaseJwtOptions
{
    public const string SectionName = "Auth";

    public string SupabaseProjectUrl { get; set; } = string.Empty;
    public string SupabaseJwtSecret { get; set; } = string.Empty;
    public List<string> AdminUserIds { get; set; } = [];
}
