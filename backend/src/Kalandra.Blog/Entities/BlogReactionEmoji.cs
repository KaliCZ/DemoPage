namespace Kalandra.Blog.Entities;

/// <summary>
/// The fixed set of reactions a reader can leave on a blog post.
/// Stored as the enum name in the event stream so renames are caught at
/// build time. The frontend maps each value to its display character.
/// </summary>
public enum BlogReactionEmoji
{
    ThumbsUp,
    Heart,
    Rocket,
    Eyes,
    Tada,
    Laugh,
}
