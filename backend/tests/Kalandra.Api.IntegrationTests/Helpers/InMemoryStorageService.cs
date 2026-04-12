using System.Collections.Concurrent;
using Kalandra.Infrastructure.Storage;

namespace Kalandra.Api.IntegrationTests.Helpers;

public class InMemoryStorageService : IStorageService
{
    private readonly ConcurrentDictionary<string, StoredFile> _files = new();

    public async Task<IReadOnlyList<StorageFileInfo>> UploadAsync(
        string folderPrefix,
        IReadOnlyList<FileUploadItem> files,
        CancellationToken ct)
    {
        var results = new List<StorageFileInfo>(files.Count);

        foreach (var file in files)
        {
            var storagePath = $"{folderPrefix}{file.FileName}";

            using var ms = new MemoryStream();
            await file.Content.CopyToAsync(ms, ct);

            _files[storagePath] = new StoredFile(ms.ToArray());

            results.Add(new StorageFileInfo(
                FileName: file.FileName,
                StoragePath: storagePath,
                FileSize: file.FileSize,
                ContentType: file.ContentType));
        }

        return results;
    }

    public Task<StorageDownloadResult?> DownloadAsync(string storagePath, CancellationToken ct)
    {
        if (!_files.TryGetValue(storagePath, out var stored))
            return Task.FromResult<StorageDownloadResult?>(null);

        return Task.FromResult<StorageDownloadResult?>(
            new StorageDownloadResult(new MemoryStream(stored.Content), stored.Content.Length));
    }


    private record StoredFile(byte[] Content);
}
