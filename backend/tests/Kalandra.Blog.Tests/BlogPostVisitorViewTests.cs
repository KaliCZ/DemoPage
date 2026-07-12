using Kalandra.Blog.Entities;

namespace Kalandra.Blog.Tests;

public class BlogPostVisitorViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Fact]
    public void IdFor_CombinesSlugAndVisitor()
    {
        var visitorId = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

        Assert.Equal($"hello-world:{visitorId}", BlogPostVisitorView.IdFor("hello-world", visitorId));
    }

    [Fact]
    public void ShouldCountNewView_WithinWindow_IsFalse()
    {
        var view = new BlogPostVisitorView { LastViewedAtUtc = Now };

        Assert.False(view.ShouldCountNewView(Now.AddMinutes(14), Window));
    }

    [Fact]
    public void ShouldCountNewView_AtOrAfterWindow_IsTrue()
    {
        var view = new BlogPostVisitorView { LastViewedAtUtc = Now };

        Assert.True(view.ShouldCountNewView(Now.AddMinutes(15), Window));
        Assert.True(view.ShouldCountNewView(Now.AddHours(2), Window));
    }
}
