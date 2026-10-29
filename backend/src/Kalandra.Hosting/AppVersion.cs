using System.Reflection;

namespace Kalandra.Hosting;

public static class AppVersion
{
    // The entry assembly, not this one: the deploy gate reads the running host's commit back out of /health/live.
    public static readonly string InformationalVersion =
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public static readonly string CommitHash =
        InformationalVersion.Contains('+')
            ? InformationalVersion[(InformationalVersion.IndexOf('+') + 1)..]
            : InformationalVersion;
}
