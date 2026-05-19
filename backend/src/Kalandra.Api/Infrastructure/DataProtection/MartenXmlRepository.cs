using System.Xml.Linq;
using Marten;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Kalandra.Api.Infrastructure.DataProtection;

// IXmlRepository is sync by contract. Block on Marten's async APIs — the repository
// is hit once at startup and again only on key rotation (~every 90 days by default),
// outside any request context where sync-over-async would matter.
public class MartenXmlRepository(IDocumentStore store) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var session = store.QuerySession();
        var keys = session.Query<DataProtectionKey>().ToListAsync().GetAwaiter().GetResult();
        return keys.Select(k => XElement.Parse(k.Xml)).ToArray();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var session = store.LightweightSession();
        session.Store(new DataProtectionKey
        {
            Id = string.IsNullOrWhiteSpace(friendlyName) ? Guid.NewGuid().ToString() : friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        session.SaveChangesAsync().GetAwaiter().GetResult();
    }
}
