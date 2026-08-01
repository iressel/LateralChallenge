using System.Text;
using System.Text.Json;
using CmsSync.Application.EventIngestion;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class CanonicalJsonTests
{
    [Theory]
    [InlineData("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}")]
    [InlineData("{ \"a\" : 1, \"b\" : [ true, null ] }", "{\"b\":[true,null],\"a\":1}")]
    [InlineData("{\"value\":\"a\"}", "{\"value\":\"\\u0061\"}")]
    [InlineData("{\"outer\":{\"a\":1,\"b\":2}}", "{\"outer\":{\"b\":2,\"a\":1}}")]
    public void EquivalentJsonProducesIdenticalCanonicalBytes(string left, string right)
    {
        Assert.Equal(Canonicalize(left), Canonicalize(right));
    }

    [Theory]
    [InlineData("[1,2]", "[2,1]")]
    [InlineData("1", "1.0")]
    [InlineData("1e0", "1")]
    [InlineData("{\"Name\":1}", "{\"name\":1}")]
    [InlineData("\"1\"", "1")]
    [InlineData("false", "null")]
    [InlineData("[]", "{}")]
    public void DistinctJsonProducesDifferentCanonicalBytes(string left, string right)
    {
        Assert.NotEqual(Canonicalize(left), Canonicalize(right));
    }

    [Fact]
    public void LengthPrefixingPreventsStringBoundaryCollisions()
    {
        var left = Canonicalize("[\"ab\",\"c\"]");
        var right = Canonicalize("[\"a\",\"bc\"]");

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void CanonicalizeRejectsDuplicateObjectProperties()
    {
        Assert.Throws<JsonException>(() => Canonicalize("{\"a\":1,\"a\":2}"));
    }

    [Fact]
    public void ValidationPreservesRawPayloadWhileUsingCanonicalEquality()
    {
        const string rawPayload = "{ \"z\" : 1, \"a\" : { \"value\" : \"x\" } }";
        var eventJson = EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{rawPayload}");

        var validated = EventIngestionTestHelper.ValidateValid(eventJson);

        Assert.Equal(rawPayload, validated.RawPayload);
        Assert.Contains(rawPayload, eventJson, StringComparison.Ordinal);
    }

    private static byte[] Canonicalize(string json) =>
        CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes(json));
}
