using System.Text;
using System.Text.Json;
using CmsSync.Application.EventIngestion;
using Xunit;

namespace CmsSync.UnitTests.EventIngestion;

public sealed class CanonicalJsonTests
{
    public static TheoryData<string, string, string> GoldenVectors =>
        new()
        {
            { "empty object", "{}", "0600000000" },
            {
                "reordered object",
                "{\"b\":2,\"a\":1}",
                "060000000200000001610300000001310000000162030000000132"
            },
            {
                "nested objects",
                "{\"outer\":{\"b\":2,\"a\":1}}",
                "0600000001000000056F75746572060000000200000001610300000001310000000162030000000132"
            },
            { "arrays", "[1,\"x\",true,null]", "05000000040300000001310400000001780200" },
            { "decoded string escape", "\"\\u0061\"", "040000000161" },
            {
                "property-name casing",
                "{\"Name\":1,\"name\":2}",
                "0600000002000000044E616D65030000000131000000046E616D65030000000132"
            },
            { "integer token", "1", "030000000131" },
            { "decimal token", "1.0", "0300000003312E30" },
            { "exponent token", "1e0", "0300000003316530" },
            { "booleans and null", "[true,false,null]", "0500000003020100" },
            { "non-ASCII names and values", "{\"ñ\":\"é\"}", "060000000100000002C3B10400000002C3A9" },
        };

    [Theory]
    [MemberData(nameof(GoldenVectors))]
    public void GoldenVectorHasExactStableUppercaseCanonicalBytesAndDoesNotMutateInput(
        string vectorName,
        string json,
        string expectedHex)
    {
        var input = Encoding.UTF8.GetBytes(json);
        var originalInput = (byte[])input.Clone();

        var first = CanonicalJson.Canonicalize(input);
        var second = CanonicalJson.Canonicalize(input);

        Assert.False(string.IsNullOrWhiteSpace(vectorName));
        Assert.Equal(expectedHex, Convert.ToHexString(first));
        Assert.Equal(first, second);
        Assert.Equal(originalInput, input);
    }

    [Theory]
    [InlineData("{\"a\":1,\"b\":2}", "{\"b\":2,\"a\":1}")]
    [InlineData("{ \"a\" : 1, \"b\" : [ true, null ] }", "{\"b\":[true,null],\"a\":1}")]
    [InlineData("{\"value\":\"a\"}", "{\"value\":\"\\u0061\"}")]
    [InlineData("{\"outer\":{\"a\":1,\"b\":2}}", "{\"outer\":{\"b\":2,\"a\":1}}")]
    public void SemanticallyEquivalentSupportedJsonProducesIdenticalCanonicalBytes(string left, string right)
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
    public void SignificantJsonDifferencesProduceDifferentCanonicalBytes(string left, string right)
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
    public void AC011ValidationPreservesRawPayloadWhileUsingCanonicalEquality()
    {
        const string rawPayload = "{ \"z\" : 1, \"a\" : { \"value\" : \"x\" } }";
        var eventJson = EventIngestionTestHelper.Publish(payloadProperty: $"\"payload\":{rawPayload}");

        var validated = EventIngestionTestHelper.ValidateValid(eventJson);

        Assert.Equal(rawPayload, validated.RawPayload);
        Assert.Contains(rawPayload, eventJson, StringComparison.Ordinal);
    }

    private static byte[] Canonicalize(string json)
    {
        return CanonicalJson.Canonicalize(Encoding.UTF8.GetBytes(json));
    }
}
