using FileTrace.Core.Models;

namespace FileTrace.Core.Tests.Models;

public class FileTypeCatalogTests
{
    [Fact]
    public void All_ContainsNoDuplicateExtensions()
    {
        var extensions = FileTypeCatalog.All.Select(d => d.Extension.ToLowerInvariant()).ToList();
        var distinct = extensions.Distinct().ToList();
        Assert.Equal(distinct.Count, extensions.Count);
    }

    [Theory]
    [InlineData("ppt")]
    [InlineData("pptx")]
    public void PptAndPptx_AreMarkedFileNameOnly_PerPlanC(string extension)
    {
        var descriptor = FileTypeCatalog.Find(extension);
        Assert.NotNull(descriptor);
        Assert.Equal(ExtractionStrategy.FileNameOnly, descriptor!.Strategy);
    }

    [Theory]
    [InlineData("doc")]
    [InlineData("docx")]
    [InlineData("xls")]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    [InlineData("rtf")]
    [InlineData("epub")]
    [InlineData("csv")]
    [InlineData("txt")]
    public void CommonFormats_AreMarkedFullText(string extension)
    {
        var descriptor = FileTypeCatalog.Find(extension);
        Assert.NotNull(descriptor);
        Assert.Equal(ExtractionStrategy.FullText, descriptor!.Strategy);
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndTrimsLeadingDot()
    {
        Assert.NotNull(FileTypeCatalog.Find("DOCX"));
        Assert.NotNull(FileTypeCatalog.Find(".docx"));
        Assert.NotNull(FileTypeCatalog.Find(".DOCX"));
    }

    [Fact]
    public void Find_UnknownExtension_ReturnsNull()
    {
        Assert.Null(FileTypeCatalog.Find("exe"));
    }

    [Fact]
    public void IsSelected_RespectsSelectedSet()
    {
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "docx", "pdf" };

        Assert.True(FileTypeCatalog.IsSelected("docx", selected));
        Assert.True(FileTypeCatalog.IsSelected(".DOCX", selected));
        Assert.False(FileTypeCatalog.IsSelected("xlsx", selected));
    }
}
