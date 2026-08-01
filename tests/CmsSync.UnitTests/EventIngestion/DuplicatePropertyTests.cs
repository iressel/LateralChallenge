using CmsSync.Application.EventIngestion;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class DuplicatePropertyTests
{
    [Theory]
    [InlineData("{\"type\":\"delete\",\"id\":\"one\",\"id\":\"two\",\"timestamp\":\"2026-07-31T12:34:56Z\"}")]
    [InlineData("{\"type\":\"publish\",\"id\":\"one\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{\"a\":1,\"a\":2}}")]
    [InlineData("{\"type\":\"publish\",\"id\":\"one\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{\"outer\":{\"a\":1,\"a\":2}}}")]
    [InlineData("{\"type\":\"delete\",\"id\":\"one\",\"timestamp\":\"2026-07-31T12:34:56Z\",\"unknown\":{\"a\":1,\"a\":2}}")]
    public void AC012DuplicateNamesMakeOnlyTheContainingItemInvalid(string eventJson)
    {
        var parser = new CmsEventArrayParser();
        var parseResult = parser.Parse(System.Text.Encoding.UTF8.GetBytes($"[{eventJson},{EventIngestionTestHelper.Delete()}]"));

        Assert.True(parseResult.IsSuccess);
        Assert.Equal(2, parseResult.Items.Count);

        var invalid = new EventValidator().Validate(parseResult.Items[0]);
        var valid = new EventValidator().Validate(parseResult.Items[1]);

        Assert.Equal(EventValidationCodes.DuplicatePropertyName, invalid.Failure?.Code);
        Assert.True(valid.IsValid);
    }

    [Theory]
    [InlineData("{\"type\":\"publish\",\"id\":\"one\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{\"left\":{\"name\":1},\"right\":{\"name\":2}}}")]
    [InlineData("{\"type\":\"publish\",\"id\":\"one\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{\"values\":[1,1,1]}}")]
    public void NamesInDifferentObjectsAndRepeatedArrayValuesRemainValid(string eventJson)
    {
        var result = EventIngestionTestHelper.ValidateSingle(eventJson);

        Assert.True(result.IsValid, result.Failure?.Code);
    }

    [Fact]
    public void EscapedAndLiteralEquivalentPropertyNamesAreDuplicates()
    {
        var eventJson = "{\"type\":\"publish\",\"id\":\"one\",\"version\":1,\"timestamp\":\"2026-07-31T12:34:56Z\",\"payload\":{\"name\":1,\"n\\u0061me\":2}}";

        var result = EventIngestionTestHelper.ValidateSingle(eventJson);

        Assert.Equal(EventValidationCodes.DuplicatePropertyName, result.Failure?.Code);
    }
}
