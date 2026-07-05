namespace Kalandra.Api.Features.Blog.Contracts;

public enum PostCommentError
{
    ContentRequired,
    ParentCommentNotFound,
    ParentCommentDeleted,
}
