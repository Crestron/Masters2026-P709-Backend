using P709.Core.Configuration;
using P709.Core.Connection;

namespace P709.Core.Tests.Connection;

public class LabSettingsValidatorTests
{
    [Theory(DisplayName = "TEST1: Accept a port within 1-65535.")]
    [InlineData(1)]
    [InlineData(LabSettings.DefaultApiPort)]
    [InlineData(65535)]
    public void Validate_WithPortInRange_DoesNotThrow(int port)
    {
        LabSettingsValidator.Validate(new LabSettings { ApiPort = port });
    }

    [Theory(DisplayName = "TEST2: Reject a port outside 1-65535, naming the setting and the file.")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_WithPortOutOfRange_Throws(int port)
    {
        var exception = Assert.Throws<InvalidConfigurationException>(() => LabSettingsValidator.Validate(new LabSettings { ApiPort = port }));

        Assert.True(exception.Message.Contains("apiPort") && exception.Message.Contains(LabSettings.FileName), $"The message should name the setting and the file: {exception.Message}");
    }

    [Fact(DisplayName = "TEST3: Reject an mcp registrationId below 1.")]
    public void Validate_WithMcpRegistrationIdBelowOne_Throws()
    {
        var settings = new LabSettings { Mcp = new McpSettings { RegistrationId = 0, Description = "Room" } };

        var exception = Assert.Throws<InvalidConfigurationException>(() => LabSettingsValidator.Validate(settings));

        Assert.True(exception.Message.Contains("mcp registrationId"), $"The message should name the setting: {exception.Message}");
    }

    [Theory(DisplayName = "TEST4: Reject a blank mcp description.")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithBlankMcpDescription_Throws(string description)
    {
        var settings = new LabSettings { Mcp = new McpSettings { Description = description } };

        var exception = Assert.Throws<InvalidConfigurationException>(() => LabSettingsValidator.Validate(settings));

        Assert.True(exception.Message.Contains("mcp description"), $"The message should name the setting: {exception.Message}");
    }

    [Theory(DisplayName = "TEST5: Accept a missing or placeholder API key, but treat it as not configured.")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(McpSettings.ApiKeyPlaceholder)]
    public void Validate_WithPlaceholderApiKey_DoesNotThrowAndIsNotConfigured(string? apiKey)
    {
        var settings = new LabSettings { Mcp = new McpSettings { Description = "Room", AnthropicApiKey = apiKey } };

        LabSettingsValidator.Validate(settings);

        Assert.True(!settings.Mcp.IsApiKeyConfigured, $"Key \"{apiKey}\" should not count as configured.");
    }
}
