using System.Reflection;

namespace Kalandra.Api.Infrastructure;

internal static class AppVersion
{
    public static readonly string InformationalVersion =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

    public static readonly string CommitHash =
        InformationalVersion.Contains('+')
            ? InformationalVersion[(InformationalVersion.IndexOf('+') + 1)..]
            : InformationalVersion;
}
