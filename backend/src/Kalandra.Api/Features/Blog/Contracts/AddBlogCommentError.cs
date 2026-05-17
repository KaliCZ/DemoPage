namespace Kalandra.Api.Features.Blog.Contracts;

/// <summary>
/// API-layer error enum returned when a comment body fails validation.
/// Stable wire contract — frontend reads the enum name as the i18n key.
/// </summary>
public enum AddBlogCommentError { ContentRequired }
