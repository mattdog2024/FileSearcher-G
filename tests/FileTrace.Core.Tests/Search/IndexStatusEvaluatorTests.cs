using FileTrace.Core.Models;
using FileTrace.Core.Search;
using Xunit;

namespace FileTrace.Core.Tests.Search;

public class IndexStatusEvaluatorTests
{
    [Theory]
    [InlineData(true, true, 10, IndexStatus.Ok)]
    [InlineData(false, true, 10, IndexStatus.SourceUnavailable)]
    [InlineData(true, false, 0, IndexStatus.NeedsUpdate)]
    [InlineData(false, false, 0, IndexStatus.SourceUnavailable)]
    [InlineData(true, true, 0, IndexStatus.NeedsUpdate)]
    public void Evaluate_PureBooleanOverload_ReturnsExpectedStatus(
        bool rootExists, bool luceneExists, long fileCount, IndexStatus expected)
    {
        var actual = IndexStatusEvaluator.Evaluate(rootExists, luceneExists, fileCount);

        Assert.Equal(expected, actual);
    }
}
