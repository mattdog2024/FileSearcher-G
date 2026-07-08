using FileTrace.Core.Search;
using Xunit;

namespace FileTrace.Core.Tests.Search;

public class HighlightMarkupParserTests
{
    [Fact]
    public void Parse_NullOrEmpty_ReturnsEmptyList()
    {
        Assert.Empty(HighlightMarkupParser.Parse(null));
        Assert.Empty(HighlightMarkupParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_PlainTextWithoutMarks_ReturnsSingleUnhighlightedSegment()
    {
        var segments = HighlightMarkupParser.Parse("没有命中任何关键词的普通文本");

        var segment = Assert.Single(segments);
        Assert.False(segment.IsHighlighted);
        Assert.Equal("没有命中任何关键词的普通文本", segment.Text);
    }

    [Fact]
    public void Parse_SingleHighlight_SplitsIntoThreeSegments()
    {
        var segments = HighlightMarkupParser.Parse("前缀<mark>寻迹</mark>后缀");

        Assert.Equal(3, segments.Count);
        Assert.Equal(("前缀", false), (segments[0].Text, segments[0].IsHighlighted));
        Assert.Equal(("寻迹", true), (segments[1].Text, segments[1].IsHighlighted));
        Assert.Equal(("后缀", false), (segments[2].Text, segments[2].IsHighlighted));
    }

    [Fact]
    public void Parse_MultipleHighlights_ParsesAllOccurrences()
    {
        var segments = HighlightMarkupParser.Parse("<mark>寻迹</mark>是一款<mark>FileTrace</mark>产品");

        Assert.Equal(4, segments.Count);
        Assert.True(segments[0].IsHighlighted);
        Assert.Equal("寻迹", segments[0].Text);
        Assert.False(segments[1].IsHighlighted);
        Assert.Equal("是一款", segments[1].Text);
        Assert.True(segments[2].IsHighlighted);
        Assert.Equal("FileTrace", segments[2].Text);
        Assert.False(segments[3].IsHighlighted);
        Assert.Equal("产品", segments[3].Text);
    }

    [Fact]
    public void Parse_HighlightAtStartAndEnd_NoEmptySegments()
    {
        var segments = HighlightMarkupParser.Parse("<mark>开头</mark>中间<mark>结尾</mark>");

        Assert.Equal(3, segments.Count);
        Assert.Equal("开头", segments[0].Text);
        Assert.Equal("中间", segments[1].Text);
        Assert.Equal("结尾", segments[2].Text);
    }

    [Fact]
    public void Parse_UnclosedTag_FallsBackToPlainTextWithoutThrowing()
    {
        var segments = HighlightMarkupParser.Parse("前缀<mark>没有闭合标签");

        Assert.Equal(2, segments.Count);
        Assert.Equal("前缀", segments[0].Text);
        Assert.False(segments[0].IsHighlighted);
        Assert.Equal("没有闭合标签", segments[1].Text);
        Assert.False(segments[1].IsHighlighted);
    }
}
