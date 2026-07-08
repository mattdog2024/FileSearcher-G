using FileTrace.Core.Models;
using FileTrace.Core.Search;
using FileTrace.Core.Tests.TestHelpers;

namespace FileTrace.Core.Tests.Search;

/// <summary>
/// 覆盖 Lucene 索引写入 + 搜索 + 高亮的端到端测试：IndexWriterService 写入的文档，
/// 通过 SearchService 按寻迹搜索语法（关键词/短语/排除词/通配符/字段前缀）能被正确检索到，
/// 且离线可用性关键前提——content 字段被存储、可供高亮——得到验证。
/// </summary>
public sealed class SearchEngineTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private void SeedIndex(string luceneDir, string profileId = "profile-1")
    {
        using var writer = new IndexWriterService(luceneDir, profileId, createNew: true);
        writer.UpdateDocument(new ExtractedDocument
        {
            FullPath = "/data/报告/2024年度总结报告.docx",
            FileName = "2024年度总结报告.docx",
            ExtensionNoDot = "docx",
            SizeBytes = 12345,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            Content = "这是一份关于文件搜索软件寻迹FileTrace的年度总结报告，内容涵盖索引引擎设计与性能优化。",
            ContentExtracted = true,
        });
        writer.UpdateDocument(new ExtractedDocument
        {
            FullPath = "/data/报告/预算表.xlsx",
            FileName = "预算表.xlsx",
            ExtensionNoDot = "xlsx",
            SizeBytes = 999,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            Content = "2024年部门预算明细表",
            ContentExtracted = true,
        });
        writer.UpdateDocument(new ExtractedDocument
        {
            FullPath = "/data/幻灯片/发布会.pptx",
            FileName = "发布会.pptx",
            ExtensionNoDot = "pptx",
            SizeBytes = 555,
            LastWriteTimeUtc = DateTimeOffset.UtcNow,
            Content = string.Empty,
            ContentExtracted = false,
        });
        writer.Commit();
    }

    [Fact]
    public void Search_KeywordHitsContent_ReturnsDocumentWithHighlight()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene1");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("寻迹");

        Assert.Equal(1, result.TotalHits);
        var item = Assert.Single(result.Items);
        Assert.Equal("2024年度总结报告.docx", item.FileName);
        Assert.NotNull(item.ContentHighlight);
        Assert.Contains("<mark>寻迹</mark>", item.ContentHighlight);
    }

    [Fact]
    public void Search_ExactPhrase_OnlyMatchesAdjacentWordOrder()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene2");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("\"年度总结\"");

        Assert.Equal(1, result.TotalHits);
        Assert.Equal("2024年度总结报告.docx", result.Items[0].FileName);
    }

    [Fact]
    public void Search_ExclusionWord_FiltersOutMatchingDocument()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene3");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        // "总结" 命中报告文档；"预算" 命中预算表；排除"总结"后应只剩预算表。
        var result = search.Search("预算 -总结");

        Assert.Equal(1, result.TotalHits);
        Assert.Equal("预算表.xlsx", result.Items[0].FileName);
    }

    [Fact]
    public void Search_WildcardExtension_MatchesFileNameKeyword()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene4");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("*.xlsx");

        Assert.Equal(1, result.TotalHits);
        Assert.Equal("预算表.xlsx", result.Items[0].FileName);
    }

    [Fact]
    public void Search_FileNamePrefix_OnlySearchesFileNameField()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene5");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        // "明细表" 只出现在预算表的内容里，不出现在文件名里；用 filename: 前缀应搜不到。
        var result = search.Search("filename:明细表");

        Assert.Equal(0, result.TotalHits);
    }

    [Fact]
    public void Search_ContentPrefix_OnlySearchesContentField()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene6");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("content:性能优化");

        Assert.Equal(1, result.TotalHits);
        Assert.Equal("2024年度总结报告.docx", result.Items[0].FileName);
    }

    [Fact]
    public void Search_NoMatchingKeyword_ReturnsEmptyResult()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene7");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("不存在的关键词xyz");

        Assert.Equal(0, result.TotalHits);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Search_FileNameOnlyDocument_IsFoundByFileNameButHasNoContentHighlight()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene8");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("发布会");

        Assert.Equal(1, result.TotalHits);
        var item = result.Items[0];
        Assert.False(item.ContentIndexed);
        Assert.Null(item.ContentHighlight);
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsEmptyResultWithoutThrowing()
    {
        string luceneDir = Path.Combine(_dir.Path, "lucene9");
        SeedIndex(luceneDir);

        using var search = new SearchService(luceneDir);
        var result = search.Search("   ");

        Assert.Equal(0, result.TotalHits);
    }

    [Fact]
    public void MultiIndexSearch_CombinesResultsFromMultipleProfilesAndTagsProfileId()
    {
        string luceneDirA = Path.Combine(_dir.Path, "multiA");
        string luceneDirB = Path.Combine(_dir.Path, "multiB");
        SeedIndex(luceneDirA, "profile-A");
        SeedIndex(luceneDirB, "profile-B");

        using var search = new SearchService(new[] { luceneDirA, luceneDirB });
        var result = search.Search("寻迹");

        // 两个索引里都有一份命中"寻迹"的文档，联合搜索应返回 2 条，且能分辨各自来源的 ProfileId。
        Assert.Equal(2, result.TotalHits);
        var profileIds = result.Items.Select(i => i.ProfileId).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "profile-A", "profile-B" }, profileIds);
    }

    [Fact]
    public void IndexWriterService_UpdateDocument_ReplacesPreviousVersionNotDuplicates()
    {
        string luceneDir = Path.Combine(_dir.Path, "update1");
        using (var writer = new IndexWriterService(luceneDir, "p1", createNew: true))
        {
            writer.UpdateDocument(new ExtractedDocument
            {
                FullPath = "/data/a.txt",
                FileName = "a.txt",
                ExtensionNoDot = "txt",
                SizeBytes = 1,
                LastWriteTimeUtc = DateTimeOffset.UtcNow,
                Content = "旧内容版本一",
                ContentExtracted = true,
            });
            writer.Commit();

            writer.UpdateDocument(new ExtractedDocument
            {
                FullPath = "/data/a.txt",
                FileName = "a.txt",
                ExtensionNoDot = "txt",
                SizeBytes = 2,
                LastWriteTimeUtc = DateTimeOffset.UtcNow,
                Content = "新内容版本二",
                ContentExtracted = true,
            });
            writer.Commit();

            Assert.Equal(1, writer.NumDocs);
        }

        using var search = new SearchService(luceneDir);
        var oldResult = search.Search("旧内容");
        var newResult = search.Search("新内容");

        Assert.Equal(0, oldResult.TotalHits);
        Assert.Equal(1, newResult.TotalHits);
    }

    [Fact]
    public void IndexWriterService_DeleteDocument_RemovesItFromSearchResults()
    {
        string luceneDir = Path.Combine(_dir.Path, "delete1");
        using (var writer = new IndexWriterService(luceneDir, "p1", createNew: true))
        {
            writer.UpdateDocument(new ExtractedDocument
            {
                FullPath = "/data/b.txt",
                FileName = "b.txt",
                ExtensionNoDot = "txt",
                SizeBytes = 1,
                LastWriteTimeUtc = DateTimeOffset.UtcNow,
                Content = "待删除文档的内容",
                ContentExtracted = true,
            });
            writer.Commit();

            writer.DeleteDocument("/data/b.txt");
            writer.Commit();
        }

        using var search = new SearchService(luceneDir);
        var result = search.Search("待删除");

        Assert.Equal(0, result.TotalHits);
    }

    public void Dispose() => _dir.Dispose();
}
