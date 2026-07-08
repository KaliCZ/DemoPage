using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;

namespace Kalandra.Blog.Tests;

public class BlogPostReadsTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 8, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid UserA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = new("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Apply_CountsEveryReadPerUserAndInTotal()
    {
        var reads = new BlogPostReads();

        reads.Apply(new BlogPostRead(UserA, Now));
        reads.Apply(new BlogPostRead(UserA, Now.AddMinutes(1)));
        reads.Apply(new BlogPostRead(UserB, Now.AddMinutes(2)));

        Assert.Equal(3, reads.TotalReads);
        Assert.Equal(2, reads.CountFor(UserA));
        Assert.Equal(1, reads.CountFor(UserB));
    }

    [Fact]
    public void CountFor_UserWhoNeverRead_IsZero()
    {
        var reads = new BlogPostReads();
        reads.Apply(new BlogPostRead(UserA, Now));

        Assert.Equal(0, reads.CountFor(Guid.NewGuid()));
    }
}
