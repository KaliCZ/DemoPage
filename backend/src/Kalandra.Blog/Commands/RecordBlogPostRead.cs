using Kalandra.Blog.Entities;
using Kalandra.Blog.Events;
using Marten;

namespace Kalandra.Blog.Commands;

public record RecordBlogPostReadCommand(Guid ReadsStreamId, Guid UserId, DateTimeOffset Timestamp);

public class RecordBlogPostReadHandler(IDocumentSession session)
{
    /// <summary>
    /// Recording a read has no failure mode — any signed-in view counts — so this
    /// returns the reader's count before this read directly instead of a Result.
    /// </summary>
    public async Task<int> RecordAndSave(RecordBlogPostReadCommand command, CancellationToken ct)
    {
        var reads = await session.LoadAsync<BlogPostReads>(command.ReadsStreamId, ct);
        session.Events.Append(command.ReadsStreamId, new BlogPostRead(command.UserId, command.Timestamp));
        await session.SaveChangesAsync(ct);
        return reads?.CountFor(command.UserId) ?? 0;
    }
}
