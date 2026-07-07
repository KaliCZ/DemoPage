using Kalandra.Infrastructure.Configuration;
using Kalandra.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Kalandra.Infrastructure.Tests;

public class ConfigProductionReadinessTests
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

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // --- Supabase ---

    [Fact]
    public void Supabase_InProduction_WithLocalhostUrl_Throws() =>
        Assert.Throws<InvalidOperationException>(() => SupabaseConfig.AddSingleton(
            new ServiceCollection(),
            Config(new() { ["Supabase:ProjectUrl"] = "http://127.0.0.1:54321", ["Supabase:ServiceKey"] = "local" }),
            Production));

    [Fact]
    public void Supabase_InProduction_WithRemoteUrl_IsAllowed() =>
        SupabaseConfig.AddSingleton(
            new ServiceCollection(),
            Config(new() { ["Supabase:ProjectUrl"] = "https://abc.supabase.co", ["Supabase:ServiceKey"] = "real" }),
            Production);

    [Fact]
    public void Supabase_InDevelopment_WithLocalhostUrl_IsAllowed() =>
        SupabaseConfig.AddSingleton(
            new ServiceCollection(),
            Config(new() { ["Supabase:ProjectUrl"] = "http://localhost:54321", ["Supabase:ServiceKey"] = "local" }),
            Development);

    // --- Turnstile ---

    [Fact]
    public void Turnstile_InProduction_WithTestKey_Throws() =>
        Assert.Throws<InvalidOperationException>(() => TurnstileConfig.AddSingleton(
            new ServiceCollection(),
            Config(new() { ["Turnstile:SecretKey"] = "1x0000000000000000000000000000000AA" }),
            Production));

    [Fact]
    public void Turnstile_InProduction_WithRealKey_IsAllowed() =>
        TurnstileConfig.AddSingleton(
            new ServiceCollection(),
            Config(new() { ["Turnstile:SecretKey"] = "a-real-secret" }),
            Production);

    [Fact]
    public void Turnstile_InDevelopment_WithTestKey_IsAllowed() =>
        TurnstileConfig.AddSingleton(
            new ServiceCollection(),
            Config(new() { ["Turnstile:SecretKey"] = "1x0000000000000000000000000000000AA" }),
            Development);

    // --- Email ---

    private static Dictionary<string, string?> EmailBase() => new()
    {
        ["Email:FromEmail"] = "blog@kalandra.tech",
        ["Email:FromName"] = "kalandra.tech",
    };

    [Fact]
    public void Email_InProduction_WithLocalhostHost_Throws()
    {
        var values = EmailBase();
        values["Email:Host"] = "localhost";
        Assert.Throws<InvalidOperationException>(() =>
            EmailConfig.AddSingleton(new ServiceCollection(), Config(values), Production));
    }

    [Fact]
    public void Email_InProduction_WithRealRelayAndLogin_IsAllowed()
    {
        var values = EmailBase();
        values["Email:Host"] = "smtp.example.com";
        values["Email:Username"] = "user";
        values["Email:Password"] = "pass";
        EmailConfig.AddSingleton(new ServiceCollection(), Config(values), Production);
    }

    [Fact]
    public void Email_WithRealRelayButNoLogin_Throws_InAnyEnvironment()
    {
        var values = EmailBase();
        values["Email:Host"] = "smtp.example.com";
        Assert.Throws<InvalidOperationException>(() =>
            EmailConfig.AddSingleton(new ServiceCollection(), Config(values), Development));
    }

    [Fact]
    public void Email_InDevelopment_WithLocalhostHost_IsAllowed()
    {
        var values = EmailBase();
        values["Email:Host"] = "localhost";
        EmailConfig.AddSingleton(new ServiceCollection(), Config(values), Development);
    }
}
