using FileTrace.Core.Models;
using FileTrace.Core.Scanning;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Scanning;

public class ManifestStoreTests
{
    [Fact]
    public void Find_NonExistentPath_ReturnsNull()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        Assert.Null(store.Find("/some/path/that/was/never/indexed.txt"));
    }

    [Fact]
    public void UpsertThenFind_RoundTripsAllFields()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        var fingerprint = new FileFingerprint
        {
            FullPath = "/docs/report.docx",
            SizeBytes = 12345,
            LastWriteTimeUtcTicks = 638000000000000000L,
            IndexedAt = DateTimeOffset.UtcNow,
            ContentIndexed = true,
        };
        store.Upsert(fingerprint);

        var found = store.Find("/docs/report.docx");
        Assert.NotNull(found);
        Assert.Equal(fingerprint.SizeBytes, found!.SizeBytes);
        Assert.Equal(fingerprint.LastWriteTimeUtcTicks, found.LastWriteTimeUtcTicks);
        Assert.True(found.ContentIndexed);
    }

    [Fact]
    public void Upsert_ExistingPath_OverwritesPreviousValues()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 100, LastWriteTimeUtcTicks = 1, ContentIndexed = false });
        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 200, LastWriteTimeUtcTicks = 2, ContentIndexed = true });

        var found = store.Find("/a.txt");
        Assert.NotNull(found);
        Assert.Equal(200, found!.SizeBytes);
        Assert.Equal(2, found.LastWriteTimeUtcTicks);
        Assert.True(found.ContentIndexed);
    }

    [Fact]
    public void IsUnchanged_SameFingerprint_ReturnsTrue()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 100, LastWriteTimeUtcTicks = 500 });

        Assert.True(store.IsUnchanged("/a.txt", 100, 500));
    }

    [Theory]
    [InlineData(999, 500)] // 大小不同
    [InlineData(100, 501)] // 修改时间不同
    public void IsUnchanged_DifferentFingerprint_ReturnsFalse(long size, long mtime)
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 100, LastWriteTimeUtcTicks = 500 });

        Assert.False(store.IsUnchanged("/a.txt", size, mtime));
    }

    [Fact]
    public void Remove_DeletesRecord()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 1, LastWriteTimeUtcTicks = 1 });
        Assert.NotNull(store.Find("/a.txt"));

        store.Remove("/a.txt");
        Assert.Null(store.Find("/a.txt"));
    }

    [Fact]
    public void GetAllPaths_ReturnsAllInsertedPaths()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 1, LastWriteTimeUtcTicks = 1 });
        store.Upsert(new FileFingerprint { FullPath = "/b.txt", SizeBytes = 1, LastWriteTimeUtcTicks = 1 });

        var all = store.GetAllPaths();
        Assert.Equal(2, all.Count);
        Assert.Contains("/a.txt", all);
        Assert.Contains("/b.txt", all);
    }

    [Fact]
    public void CountRecords_ReflectsNumberOfDistinctPaths()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        Assert.Equal(0, store.CountRecords());

        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 1, LastWriteTimeUtcTicks = 1 });
        store.Upsert(new FileFingerprint { FullPath = "/a.txt", SizeBytes = 2, LastWriteTimeUtcTicks = 2 }); // 同路径更新，不应重复计数
        store.Upsert(new FileFingerprint { FullPath = "/b.txt", SizeBytes = 1, LastWriteTimeUtcTicks = 1 });

        Assert.Equal(2, store.CountRecords());
    }

    [Fact]
    public void UpsertBatch_InsertsAllRecordsInSingleTransaction()
    {
        using var dir = new TempDirectory();
        using var store = ManifestStore.Open(System.IO.Path.Combine(dir.Path, "manifest.db"));

        var batch = Enumerable.Range(0, 50)
            .Select(i => new FileFingerprint { FullPath = $"/file{i}.txt", SizeBytes = i, LastWriteTimeUtcTicks = i })
            .ToList();

        store.UpsertBatch(batch);

        Assert.Equal(50, store.CountRecords());
    }

    [Fact]
    public void Open_PersistsAcrossReopens()
    {
        using var dir = new TempDirectory();
        string dbPath = System.IO.Path.Combine(dir.Path, "manifest.db");

        using (var store = ManifestStore.Open(dbPath))
        {
            store.Upsert(new FileFingerprint { FullPath = "/persisted.txt", SizeBytes = 42, LastWriteTimeUtcTicks = 42 });
        }

        using (var reopened = ManifestStore.Open(dbPath))
        {
            var found = reopened.Find("/persisted.txt");
            Assert.NotNull(found);
            Assert.Equal(42, found!.SizeBytes);
        }
    }
}
