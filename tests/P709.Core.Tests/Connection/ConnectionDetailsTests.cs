using P709.Core.Connection;

namespace P709.Core.Tests.Connection;

public class ConnectionDetailsTests
{
    [Fact(DisplayName = "TEST4: Build the hub URL from the host and port.")]
    public void For_WithHostAndPort_BuildsHubUrl()
    {
        var actual = ConnectionDetails.For("10.0.0.5", 5081);

        Assert.True("http://10.0.0.5:5081/hubs/room" == actual?.HubUrl, $"Unexpected URL: {actual?.HubUrl}");
    }

    [Theory(DisplayName = "TEST5: Return null while no host address is available.")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void For_WithNoHost_ReturnsNull(string? host)
    {
        var actual = ConnectionDetails.For(host, 5000);

        Assert.True(actual is null, "Expected no details when the host address is unknown.");
    }

    [Fact(DisplayName = "TEST1: Serialise connection details to camelCase JSON.")]
    public void ToJson_WithHubUrl_UsesCamelCasePropertyName()
    {
        var details = new ConnectionDetails("http://10.0.0.5:5000/hubs/room");

        var actual = details.ToJson();

        Assert.True("{\"hubUrl\":\"http://10.0.0.5:5000/hubs/room\"}" == actual, $"Unexpected JSON: {actual}");
    }

    [Fact(DisplayName = "TEST2: Round-trip connection details through JSON.")]
    public void FromJson_WithToJsonOutput_ReturnsEqualDetails()
    {
        var expected = new ConnectionDetails("http://10.0.0.5:5000/hubs/room");

        var actual = ConnectionDetails.FromJson(expected.ToJson());

        Assert.True(expected == actual, "Round-tripped details differ from the original.");
    }

    [Theory(DisplayName = "TEST3: Reject empty JSON.")]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_WithBlankJson_Throws(string json)
    {
        Assert.Throws<ArgumentException>(() => ConnectionDetails.FromJson(json));
    }
}
