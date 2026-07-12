namespace Kalandra.Blog.Entities;

/// <summary>
/// The reaction streams a visitor has reacted on while anonymous, so sign-in can
/// fold just those into the account rather than touching every post (see LinkVisitor).
/// </summary>
public class VisitorReactions
{
    public Guid Id { get; set; }
    public HashSet<Guid> ReactionStreamIds { get; set; } = [];
}
