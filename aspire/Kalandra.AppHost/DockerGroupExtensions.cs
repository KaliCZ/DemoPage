using Aspire.Hosting.ApplicationModel;

namespace Kalandra.AppHost;

internal static class DockerGroupExtensions
{
    // Docker Desktop groups containers sharing com.docker.compose.project under one row, even outside compose.
    public static IResourceBuilder<T> WithDockerGroup<T>(this IResourceBuilder<T> builder, string project)
        where T : ContainerResource
        => builder.WithContainerRuntimeArgs("--label", $"com.docker.compose.project={project}");
}
