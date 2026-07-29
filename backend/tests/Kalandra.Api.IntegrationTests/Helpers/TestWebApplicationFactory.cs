using Kalandra.Blog;
using Kalandra.Blog.Feed;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Email;
using Kalandra.Infrastructure.Storage;
using Kalandra.Infrastructure.Turnstile;
using Kalandra.Infrastructure.Users;
using Marten;
using Marten.Events;
using Marten.Events.Daemon.Coordination;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Testcontainers.PostgreSql;

namespace Kalandra.Api.IntegrationTests.Helpers;

public class TestWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The MCP list_blog_posts tool reads this instead of a live frontend; StubBlogFeedHandler serves it.
    public const string BlogFeedSlug = "zero-code-validations-in-your-dotnet-api";
    private const string BlogFeedRss =
        $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel><title>Blog</title>
        <item>
          <title>[EN] Zero-code validations</title>
          <link>https://www.kalandra.tech/blog/{BlogFeedSlug}</link>
          <description>A summary.</description>
          <pubDate>Tue, 01 Jul 2025 00:00:00 GMT</pubDate>
          <category>dotnet</category>
        </item>
        </channel></rss>
        """;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    // Generous because CI runs Docker and several test hosts in parallel; progress-based waits return
    // the moment the daemon catches up, so the cap only bites when something is genuinely broken.
    private static readonly TimeSpan AsyncDaemonTimeout = TimeSpan.FromSeconds(60);

    public FakeSupabaseAdminService FakeAdminService { get; } = new();
    public TestEmailSender EmailSender { get; } = new();
    public FakeUserInfoService UserInfoService { get; } = new();

    /// <summary>
    /// Waits until the async daemon has processed every event committed so far, then returns the sent
    /// emails matching the predicate. The result is final for those events — a missing or extra
    /// notification is a real bug, not a race — so callers assert exact counts without sleeping.
    /// </summary>
    public async Task<EmailMessage[]> WaitForDeliveredEmailsAsync(Func<EmailMessage, bool> predicate)
    {
        var store = Services.GetRequiredService<IDocumentStore>();
        await store.WaitForNonStaleProjectionDataAsync(AsyncDaemonTimeout);
        return [.. EmailSender.Sent.Where(predicate)];
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Mirror the dev/e2e environment so the configs' prod-only localhost checks stay out of the test host.
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("Supabase:ProjectUrl", "https://test-project.supabase.co");
        builder.UseSetting("Supabase:ServiceKey", "test-service-key");
        builder.UseSetting("Blog:AuthorNotificationEmail", "author@kalandra.local");
        builder.UseSetting("JobOffers:OwnerNotificationEmail", "owner@kalandra.local");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(EmailSender);

            // Random per-test slugs need to resolve; the prod catalog gates to real posts.
            services.RemoveAll<IBlogPostCatalog>();
            services.AddSingleton<IBlogPostCatalog, TestBlogPostCatalog>();

            services.RemoveAll<IStorageService>();
            services.AddSingleton<IStorageService, InMemoryStorageService>();

            services.RemoveAll<ITurnstileValidator>();
            services.AddSingleton<ITurnstileValidator, AlwaysPassTurnstileValidator>();

            services.RemoveAll<ISupabaseAdminService>();
            services.AddSingleton<ISupabaseAdminService>(FakeAdminService);

            services.RemoveAll<IUserInfoService>();
            services.AddSingleton<IUserInfoService>(UserInfoService);

            services.RemoveAll<Supabase.Storage.Client>();
            services.RemoveAll<Supabase.Gotrue.Interfaces.IGotrueAdminClient<Supabase.Gotrue.User>>();

            // No live frontend in tests — serve the MCP list_blog_posts tool a canned RSS feed.
            services.AddHttpClient<BlogFeedClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new StubBlogFeedHandler(BlogFeedRss));

            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    var handler = new FakeJwksHandler(JwtTestHelper.TestIssuer);
                    var httpClient = new HttpClient(handler);
                    var metadataAddress = $"{JwtTestHelper.TestIssuer}/.well-known/openid-configuration";

                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        metadataAddress: metadataAddress,
                        configRetriever: new OpenIdConnectConfigurationRetriever(),
                        docRetriever: new HttpDocumentRetriever(httpClient) { RequireHttps = true });
                });
        });
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        // Bypass hire-me rate limiter in tests — same mechanism the frontend uses
        // after the user solves the interactive Turnstile challenge.
        client.DefaultRequestHeaders.Add("X-Interactive-Captcha", "1");
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        await WaitForNotificationSubscriptionsToStartAsync();
    }

    // SubscribeFromPresent seeds each subscription at the current high-water mark when its shard starts —
    // an event committed before that would be skipped, never emailed. Agents register only after seeding,
    // so waiting for registration while the database is still empty guarantees no test event is missed.
    private async Task WaitForNotificationSubscriptionsToStartAsync()
    {
        var store = (DocumentStore)Services.GetRequiredService<IDocumentStore>();
        var expectedShards = store.Options.Projections.AllShards().Select(shard => shard.Name.Identity).ToHashSet();
        var daemon = Services.GetRequiredService<IProjectionCoordinator>().DaemonForMainDatabase();

        using var cancellation = new CancellationTokenSource(AsyncDaemonTimeout);
        try
        {
            while (!expectedShards.IsSubsetOf(daemon.CurrentAgents().Select(agent => agent.Name.Identity)))
                await Task.Delay(100, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"The async daemon did not start subscriptions [{string.Join(", ", expectedShards)}] within {AsyncDaemonTimeout}.");
        }
    }

    public new async ValueTask DisposeAsync()
    {
        // Stop the host — and its async daemon — before the database it talks to, so daemon shutdown
        // doesn't log connection failures into an already-disposed logger and fault the fixture teardown.
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private sealed class StubBlogFeedHandler(string rss) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(rss, System.Text.Encoding.UTF8, "application/rss+xml"),
            });
    }
}
