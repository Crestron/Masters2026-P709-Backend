using Newtonsoft.Json;
using P709.Core.Configuration;
using P709.Core.Nvx;
using static P709.Core.Tests.Configuration.RoomConfigTestData;

namespace P709.Core.Tests.Nvx;

public class NvxConfigTests
{
    private static void AssertInvalid(NvxConfig nvx, string expectedFragment)
    {
        var exception = Assert.Throws<InvalidConfigurationException>(() => RoomConfigValidator.Validate(CreateConfig(nvx: nvx)));

        Assert.True(exception.Message.Contains(expectedFragment), $"Expected the message to mention '{expectedFragment}', was: {exception.Message}");
    }

    [Fact(DisplayName = "TEST1: Accept a room with no nvx section.")]
    public void Validate_WithNoNvx_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(nvx: null));
    }

    [Fact(DisplayName = "TEST2: Accept a complete nvx section.")]
    public void Validate_WithValidNvx_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(nvx: CreateNvx()));
    }

    [Fact(DisplayName = "TEST3: Accept a receiver host name as well as an IP address.")]
    public void Validate_WithHostNames_DoesNotThrow()
    {
        RoomConfigValidator.Validate(CreateConfig(nvx: CreateNvx(receiverAddress: "nvx-rx-01.local")));
    }

    [Fact(DisplayName = "TEST4: Reject a blank username.")]
    public void Validate_WithBlankUsername_Throws()
    {
        AssertInvalid(CreateNvx(username: " "), "nvx username");
    }

    [Theory(DisplayName = "TEST5: Reject a receiver address that is not an IP address or host name.")]
    [InlineData("")]
    [InlineData("http://10.0.0.50")]
    [InlineData("10.0.0.50:443")]
    public void Validate_WithBadReceiverAddress_Throws(string address)
    {
        AssertInvalid(CreateNvx(receiverAddress: address), "nvx receiverAddress");
    }

    [Fact(DisplayName = "TEST6: Reject an nvx section that routes no sources.")]
    public void Validate_WithNoSources_Throws()
    {
        AssertInvalid(CreateNvx(sources: []), "at least one entry");
    }

    [Fact(DisplayName = "TEST7: Reject a route for a source the room does not have.")]
    public void Validate_WithUnknownSource_Throws()
    {
        AssertInvalid(CreateNvx(sources: [CreateNvxSource(sourceId: "nope")]), "nvx source \"nope\" is not one of the room's sources");
    }

    [Fact(DisplayName = "TEST8: Reject two routes for the same source.")]
    public void Validate_WithDuplicateSource_Throws()
    {
        AssertInvalid(CreateNvx(sources: [CreateNvxSource(), CreateNvxSource(multicastAddress: "239.8.0.22")]), "more than one entry for source \"tv\"");
    }

    [Fact(DisplayName = "TEST9: Read the nvx section from JSON, including the password.")]
    public void Deserialize_ReadsPassword()
    {
        const string json = """{"username":"admin","password":"secret","receiverAddress":"10.0.0.50","sources":[{"sourceId":"tv","multicastAddress":"239.8.0.21"}]}""";

        var actual = JsonConvert.DeserializeObject<NvxConfig>(json)!;

        Assert.True("secret" == actual.Password, "The password should be read from the file.");
        Assert.True("239.8.0.21" == actual.FindSource("tv")?.MulticastAddress, "The source route should be read from the file.");
    }

    [Fact(DisplayName = "TEST10: Never write the password back out, so it is not sent to the front-end.")]
    public void Serialize_OmitsPassword()
    {
        var json = JsonConvert.SerializeObject(CreateNvx(password: "secret"));

        Assert.True(!json.Contains("secret") && !json.Contains("Password"), $"The password leaked: {json}");
    }
}
