using StrongTypes;

namespace Kalandra.Infrastructure.Storage;

public interface IStorageFileVerifier
{
    Task<Try<IReadOnlyList<StorageFileInfo>, FileVerificationError>> VerifyAsync(
        string expectedFolderPrefix,
        IReadOnlyList<StorageFileInfo>? files,
        CancellationToken ct);
}
