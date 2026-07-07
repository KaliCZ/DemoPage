using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kalandra.Blog.Tests;

public class BlogNotificationsConfigTests
{
    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static readonly IHostEnvironment Development = new FakeHostEnvironment("Development");
    private static readonly IHostEnvironment Production = new FakeHostEnvironment("Production");

    private static IConfiguration Config(string authorEmail) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Blog:AuthorNotificationEmail"] = authorEmail })
            .Build();

    [Fact]
    public void InProduction_WithLocalPlaceholderAddress_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            BlogNotificationsConfig.AddSingleton(new ServiceCollection(), Config("author@kalandra.local"), Production));

    [Fact]
    public void InProduction_WithRealAddress_IsAllowed() =>
        BlogNotificationsConfig.AddSingleton(new ServiceCollection(), Config("author@kalandra.tech"), Production);

    [Fact]
    public void InDevelopment_WithLocalPlaceholderAddress_IsAllowed() =>
        BlogNotificationsConfig.AddSingleton(new ServiceCollection(), Config("author@kalandra.local"), Development);
}
