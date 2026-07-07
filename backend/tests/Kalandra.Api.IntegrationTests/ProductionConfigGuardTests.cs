using Kalandra.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace Kalandra.Api.IntegrationTests;

public class ProductionConfigGuardTests
{
    private sealed class FakeEnv(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> RemoteConfig() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=db.example.com;Database=app",
        ["Supabase:ProjectUrl"] = "https://abc.supabase.co",
        ["Email:Host"] = "smtp.example.com",
        ["Turnstile:SecretKey"] = "a-real-secret",
    };

    [Fact]
    public void Development_SkipsTheGuard_EvenOnLocalConfig() =>
        ProductionConfigGuard.Validate(Config(new() { ["Email:Host"] = "localhost" }), new FakeEnv("Development"));

    [Fact]
    public void Production_WithRemoteConfig_Passes() =>
        ProductionConfigGuard.Validate(Config(RemoteConfig()), new FakeEnv("Production"));

    [Fact]
    public void Production_WithLocalEmailHost_Throws()
    {
        var config = RemoteConfig();
        config["Email:Host"] = "localhost";
        Assert.Throws<InvalidOperationException>(() => ProductionConfigGuard.Validate(Config(config), new FakeEnv("Production")));
    }

    [Fact]
    public void Production_WithMissingTurnstileSecret_Throws()
    {
        var config = RemoteConfig();
        config.Remove("Turnstile:SecretKey");
        Assert.Throws<InvalidOperationException>(() => ProductionConfigGuard.Validate(Config(config), new FakeEnv("Production")));
    }
}
