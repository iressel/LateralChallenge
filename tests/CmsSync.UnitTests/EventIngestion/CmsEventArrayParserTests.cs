using System.Text;
using CmsSync.Application.EventIngestion;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class CmsEventArrayParserTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void ParseAcceptsSupportedBatchSizesAndPreservesSequence(int itemCount)
    {
        var json = $"[{string.Join(',', Enumerable.Repeat("{}", itemCount))}]";

        var result = Parse(json);

        Assert.True(result.IsSuccess);
        Assert.Equal(itemCount, result.Items.Count);
        Assert.Equal(Enumerable.Range(0, itemCount), result.Items.Select(item => item.Sequence));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]")]
    public void ParseRejectsUnsupportedBatchSizesWithoutReturningItems(string json)
    {
        var result = Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal(CmsEventParsingCodes.BatchSizeOutOfRange, result.Failure?.Code);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("[")]
    [InlineData("[{}")]
    [InlineData("[{},]")]
    [InlineData("[{}] trailing")]
    public void ParseRejectsMalformedJsonWithoutReturningItems(string json)
    {
        var result = Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal(CmsEventParsingCodes.MalformedJson, result.Failure?.Code);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("\"value\"")]
    [InlineData("42")]
    [InlineData("{\"events\":[{}]}")]
    public void ParseRejectsNonArrayTopLevelValues(string json)
    {
        var result = Parse(json);

        Assert.False(result.IsSuccess);
        Assert.Equal(CmsEventParsingCodes.InvalidEnvelope, result.Failure?.Code);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void ParseAcceptsTheExactRequestSizeLimitAndRejectsOneByteMore()
    {
        var parser = new CmsEventArrayParser();
        var accepted = Encoding.UTF8.GetBytes(
            new string(' ', parser.MaximumRequestSizeBytes - "[{}]".Length) + "[{}]");
        var rejected = new byte[accepted.Length + 1];
        accepted.CopyTo(rejected, 0);
        rejected[^1] = (byte)' ';

        var acceptedResult = parser.Parse(accepted);
        var rejectedResult = parser.Parse(rejected);

        Assert.True(acceptedResult.IsSuccess);
        Assert.Equal(CmsEventParsingCodes.RequestTooLarge, rejectedResult.Failure?.Code);
        Assert.Empty(rejectedResult.Items);
    }

    [Fact]
    public void ParseEnforcesTheConfiguredSafeMaximumDepth()
    {
        var limits = new CmsEventIngestionLimits(maximumJsonDepth: 4);
        var parser = new CmsEventArrayParser(limits);

        var result = parser.Parse("[{\"a\":{\"b\":{\"c\":{}}}}]"u8.ToArray());

        Assert.False(result.IsSuccess);
        Assert.Equal(CmsEventParsingCodes.MalformedJson, result.Failure?.Code);
        Assert.Empty(result.Items);
    }

    private static CmsEventArrayParseResult Parse(string json) =>
        new CmsEventArrayParser().Parse(Encoding.UTF8.GetBytes(json));
}
