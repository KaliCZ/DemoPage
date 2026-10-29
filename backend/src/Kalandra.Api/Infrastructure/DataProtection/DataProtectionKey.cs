namespace Kalandra.Api.Infrastructure.DataProtection;

// Persisted so payloads encrypted before a deploy still decrypt after the container is replaced.
public class DataProtectionKey
{
    public string Id { get; set; } = default!;
    public string Xml { get; set; } = default!;
}
