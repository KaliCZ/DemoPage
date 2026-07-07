using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kalandra.JobOffers.Tests;

public class JobOffersNotificationsConfigTests
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

    private static IConfiguration Config(string ownerEmail) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["JobOffers:OwnerNotificationEmail"] = ownerEmail })
            .Build();

    [Fact]
    public void InProduction_WithLocalPlaceholderAddress_Throws() =>
        Assert.Throws<InvalidOperationException>(() =>
            JobOffersNotificationsConfig.AddSingleton(new ServiceCollection(), Config("owner@kalandra.local"), Production));

    [Fact]
    public void InProduction_WithRealAddress_IsAllowed() =>
        JobOffersNotificationsConfig.AddSingleton(new ServiceCollection(), Config("owner@kalandra.tech"), Production);

    [Fact]
    public void InDevelopment_WithLocalPlaceholderAddress_IsAllowed() =>
        JobOffersNotificationsConfig.AddSingleton(new ServiceCollection(), Config("owner@kalandra.local"), Development);
}
