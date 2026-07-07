using Kalandra.Blog.Entities;
using Kalandra.Infrastructure.Users;
using Marten;

namespace Kalandra.Blog.Queries;

public record GetBlogCommentsQuery(Guid CommentsStreamId);

public class GetBlogCommentsHandler(IQuerySession session, IUserInfoService userInfoService)
{
    public async Task<BlogPostComments> Get(GetBlogCommentsQuery query, CancellationToken ct) =>
        await session.Events.AggregateStreamAsync<BlogPostComments>(query.CommentsStreamId, token: ct)
            ?? new BlogPostComments();

    /// <summary>Overlays each author's current profile so a later name/avatar change shows on their old comments; separate from <see cref="Get"/> so the notification workflow's read doesn't do profile lookups.</summary>
    public async Task<BlogPostComments> GetForDisplay(GetBlogCommentsQuery query, CancellationToken ct)
    {
        var comments = await Get(query, ct);

        var authorIds = comments.Comments.Where(c => !c.IsDeleted).Select(c => c.UserId);
        var profiles = await userInfoService.GetUserInfoAsync(authorIds, ct);

        foreach (var comment in comments.Comments.Where(c => !c.IsDeleted))
        {
            if (!profiles.TryGetValue(comment.UserId, out var profile))
                continue;

            comment.AuthorDisplayName = profile.DisplayName.AsNonEmpty() ?? comment.AuthorDisplayName;
            // A resolved profile with no avatar means it was cleared — reflect null, don't keep the stale stored value.
            comment.AuthorAvatarUrl = profile.AvatarUrl;
        }

        return comments;
    }
}
