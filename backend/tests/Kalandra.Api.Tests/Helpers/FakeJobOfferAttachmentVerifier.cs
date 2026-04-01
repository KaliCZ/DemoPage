using Kalandra.Infrastructure.Storage;
using StrongTypes;

namespace Kalandra.Api.Tests.Helpers;

public class FakeStorageFileVerifier : IStorageFileVerifier
{
    public Task<Try<IReadOnlyList<StorageFileInfo>, FileVerificationError>> VerifyAsync(
        string expectedFolderPrefix,
        IReadOnlyList<StorageFileInfo>? files,
        CancellationToken ct)
    {
        if (files == null || files.Count == 0)
        {
            return Task.FromResult(
                Try.Success<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                    Array.Empty<StorageFileInfo>()));
        }

        foreach (var file in files)
        {
            if (file.StoragePath.Contains("/missing/", StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                        FileVerificationError.FileNotFound));
            }

            if (!file.StoragePath.StartsWith(expectedFolderPrefix, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                        FileVerificationError.WrongFolder));
            }

            var fileName = Path.GetFileName(file.StoragePath.Replace('\\', '/'));
            if (!string.Equals(fileName, file.FileName, StringComparison.Ordinal))
            {
                return Task.FromResult(
                    Try.Error<IReadOnlyList<StorageFileInfo>, FileVerificationError>(
                        FileVerificationError.MetadataMismatch));
            }
        }

        return Task.FromResult(
            Try.Success<IReadOnlyList<StorageFileInfo>, FileVerificationError>(files));
    }
}
