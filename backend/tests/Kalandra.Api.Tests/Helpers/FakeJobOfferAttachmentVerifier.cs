using Kalandra.Infrastructure.Storage;

namespace Kalandra.Api.Tests.Helpers;

public class FakeStorageFileUploader : IStorageFileUploader
{
    public Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct)
    {
        var results = files.Select(f => new StorageFileInfo(
            FileName: f.FileName,
            StoragePath: $"{folderPrefix}{f.FileName}",
            FileSize: f.FileSize,
            ContentType: f.ContentType)).ToList();

        return Task.FromResult<IReadOnlyList<StorageFileInfo>>(results);
    }
}
