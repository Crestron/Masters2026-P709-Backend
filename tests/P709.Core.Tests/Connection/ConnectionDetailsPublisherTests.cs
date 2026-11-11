using P709.Core.Connection;

namespace P709.Core.Tests.Connection;

public class ConnectionDetailsPublisherTests
{
    private readonly RecordingPanelSerialOutput _output = new();

    private ConnectionDetailsPublisher CreatePublisher(string? hostAddress = "10.0.0.5") =>
        new(5000, new FakeHostAddressProvider(hostAddress), _output);

    [Fact(DisplayName = "TEST1: Publish the connection details JSON on serial join 1.")]
    public void Publish_WithHostAddress_SendsJsonOnSerialJoin1()
    {
        var result = CreatePublisher().Publish();

        Assert.True(result, "Publish should report success.");
        Assert.True(1 == _output.Sent.Count, $"Expected one value sent, got {_output.Sent.Count}.");
        Assert.True(1u == _output.Sent[0].Join, $"Expected join 1, got {_output.Sent[0].Join}.");
        Assert.True("http://10.0.0.5:5000/hubs/room" == ConnectionDetails.FromJson(_output.Sent[0].Value).HubUrl, "Hub URL in payload is wrong.");
    }

    [Fact(DisplayName = "TEST2: Send nothing while no host address is available.")]
    public void Publish_WithNoHostAddress_SendsNothingAndReturnsFalse()
    {
        var result = CreatePublisher(hostAddress: null).Publish();

        Assert.True(!result, "Publish should report failure.");
        Assert.True(0 == _output.Sent.Count, "Nothing should be sent.");
    }

    [Fact(DisplayName = "TEST3: Publish again each time the panel reconnects.")]
    public void Publish_CalledTwice_SendsTwice()
    {
        var publisher = CreatePublisher();

        publisher.Publish();
        publisher.Publish();

        Assert.True(2 == _output.Sent.Count, $"Expected two values sent, got {_output.Sent.Count}.");
    }

    [Fact(DisplayName = "TEST4: The panel IPID and serial join match the lab specification.")]
    public void PanelJoins_Constants_MatchLabSpecification()
    {
        Assert.True(0x03u == PanelJoins.PanelIpId, "Panel IPID must be 03.");
        Assert.True(1u == PanelJoins.ConnectionDetailsSerial, "Connection details must be on serial join 1.");
    }
}
