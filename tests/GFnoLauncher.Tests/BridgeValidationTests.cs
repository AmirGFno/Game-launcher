using System.Text.Json;
using GameLauncher.Bridge;
using Xunit;

namespace GFnoLauncher.Tests;

public class BridgeValidationTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"version\":\"1\"}")]
    [InlineData("{\"version\":1,\"source\":42}")]
    [InlineData("{\"version\":1,\"source\":\"gfno-app\",\"type\":[]}")]
    [InlineData("{\"version\":1,\"source\":\"gfno-app\",\"type\":\"library.get\",\"requestId\":42}")]
    public void MalformedEnvelopeDoesNotThrow(string json)
        => Assert.False(BridgeMessageParser.TryParse(json, out _, out _));

    [Fact]
    public void ValidEnvelopeParses()
        => Assert.True(BridgeMessageParser.TryParse("{\"version\":1,\"source\":\"gfno-app\",\"type\":\"library.get\",\"requestId\":\"test\",\"payload\":{}}", out _, out _));

    [Fact]
    public void MissingOptionalPropertyDoesNotThrow()
    {
        var payload = JsonSerializer.SerializeToElement(new { });
        Assert.Null(BridgePayload.GetString(payload, "missing"));
        Assert.Equal(1500, BridgePayload.GetInt32(payload, "interval", 1500));
    }
}
