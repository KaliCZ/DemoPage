using Kalandra.Blog.Entities;
using Kalandra.Infrastructure.Auth;
using Kalandra.Infrastructure.Users;
using Marten;

namespace Kalandra.Blog.Queries;

public record ListMyBlogCommentsQuery(CurrentUser User);

public record MyBlogComment(BlogPost Post, BlogPostComment Comment, IReadOnlyList<BlogPostComment> Replies);

public class ListMyBlogCommentsHandler(
    IQuerySession session,
    IBlogPostCatalog postCatalog,
    IUserInfoService userInfoService)
{
    public async Task<IReadOnlyList<MyBlogComment>> List(ListMyBlogCommentsQuery query, CancellationToken ct)
    {
        var results = new List<MyBlogComment>();
        foreach (var post in postCatalog.All)
        {
            var comments = await session.Events.AggregateStreamAsync<BlogPostComments>(post.CommentsStreamId, token: ct);
            if (comments == null)
                continue;

            results.AddRange(Collect(post, comments, query.User.Id));
        }

        await OverlayAuthorProfiles(results, ct);

        return results.OrderByDescending(r => r.Comment.PostedAt).ToList();
    }

    /// <summary>One post's contribution: my non-deleted comments, each with its direct non-deleted replies.</summary>
    public static IEnumerable<MyBlogComment> Collect(BlogPost post, BlogPostComments comments, Guid userId) =>
        comments.Comments
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .Select(c => new MyBlogComment(
                Post: post,
                Comment: c,
                Replies: comments.Comments
                    .Where(r => r.ParentCommentId == c.CommentId && !r.IsDeleted)
                    .OrderBy(r => r.PostedAt)
                    .ToList()));

    // Same reason as GetBlogCommentsHandler.GetForDisplay: show each author's current profile, not the one stored at post time.
    private async Task OverlayAuthorProfiles(List<MyBlogComment> results, CancellationToken ct)
    {
        var comments = results.SelectMany(r => r.Replies.Prepend(r.Comment)).Distinct().ToList();
        var profiles = await userInfoService.GetUserInfoAsync(comments.Select(c => c.UserId).Distinct(), ct);

        foreach (var comment in comments)
        {
            if (!profiles.TryGetValue(comment.UserId, out var profile))
                continue;

            comment.AuthorDisplayName = profile.DisplayName.AsNonEmpty() ?? comment.AuthorDisplayName;
            comment.AuthorAvatarUrl = profile.AvatarUrl;
        }
    }
}
