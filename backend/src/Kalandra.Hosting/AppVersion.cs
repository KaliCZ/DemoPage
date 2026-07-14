using System.Reflection;

namespace Kalandra.Hosting;

public static class AppVersion
{
    // The entry assembly, not this one: the version (and its embedded SourceRevisionId) belongs to
    // whichever host is running, and the deploy gate reads that commit back out of /health/live.
    public static readonly string InformationalVersion =
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public static readonly string CommitHash =
        InformationalVersion.Contains('+')
            ? InformationalVersion[(InformationalVersion.IndexOf('+') + 1)..]
            : InformationalVersion;
}
