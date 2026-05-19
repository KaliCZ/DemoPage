namespace Kalandra.Api.Infrastructure.DataProtection;

// Marten-backed storage for ASP.NET Core data-protection keys. Keys must survive
// container destruction so cookies/antiforgery payloads encrypted before a deploy
// can still be decrypted after it.
public class DataProtectionKey
{
    public string Id { get; set; } = default!;
    public string Xml { get; set; } = default!;
}
