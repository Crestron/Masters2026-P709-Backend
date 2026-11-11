using P709.Core.Configuration;
using P709.Core.Connection;

namespace P709.Core.Tests.Connection;

public class LabSettingsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("p709-settings-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FolderWith(string contents)
    {
        var path = Path.Combine(_directory, LabSettings.FileName);
        File.WriteAllText(path, contents);

        return _directory;
    }

    [Fact(DisplayName = "TEST1: Use the default port when the file does not set one.")]
    public void Load_WithNoPortConfigured_UsesDefaultPort()
    {
        var actual = LabSettings.Load(FolderWith("{}"));

        Assert.True(LabSettings.DefaultApiPort == actual.ApiPort, $"Expected the default port, got {actual.ApiPort}.");
    }

    [Fact(DisplayName = "TEST2: Read the configured port.")]
    public void Load_WithApiPort_ReturnsThatPort()
    {
        var actual = LabSettings.Load(FolderWith("{\"apiPort\": 5081}"));

        Assert.True(5081 == actual.ApiPort, $"Expected 5081, got {actual.ApiPort}.");
    }

    [Theory(DisplayName = "TEST3: Reject a port outside 1-65535.")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Load_WithOutOfRangePort_Throws(int port)
    {
        Assert.Throws<InvalidConfigurationException>(() => LabSettings.Load(FolderWith($"{{\"apiPort\": {port}}}")));
    }

    [Fact(DisplayName = "TEST4: Reject an unknown setting so a typo is not silently ignored.")]
    public void Load_WithUnknownSetting_Throws()
    {
        Assert.Throws<InvalidConfigurationException>(() => LabSettings.Load(FolderWith("{\"apiPrt\": 5081}")));
    }

    [Fact(DisplayName = "TEST5: Fail with FileNotFoundException when the file is missing.")]
    public void Load_WithMissingFile_Throws()
    {
        var exception = Assert.Throws<FileNotFoundException>(() => LabSettings.Load(_directory));

        Assert.True(exception.Message.Contains(LabSettings.FileName), $"The message should name the file: {exception.Message}");
    }

    [Theory(DisplayName = "TEST6: Fail with InvalidConfigurationException for an empty or malformed file.")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ not json")]
    public void Load_WithUnusableJson_Throws(string contents)
    {
        Assert.Throws<InvalidConfigurationException>(() => LabSettings.Load(FolderWith(contents)));
    }

    [Fact(DisplayName = "TEST7: Leave MCP off when the file has no mcp section.")]
    public void Load_WithNoMcpSection_HasNoMcpSettings()
    {
        var actual = LabSettings.Load(FolderWith("{}"));

        Assert.True(actual.Mcp is null, "Expected no MCP settings.");
    }

    [Fact(DisplayName = "TEST8: Read the mcp section.")]
    public void Load_WithMcpSection_ReadsIt()
    {
        var actual = LabSettings.Load(FolderWith("{\"mcp\": {\"registrationId\": 2, \"description\": \"Room\", \"anthropicApiKey\": \"k\"}}"));

        Assert.True(actual.Mcp is { RegistrationId: 2, Description: "Room", AnthropicApiKey: "k", IsApiKeyConfigured: true },
            $"Unexpected MCP settings: {actual.Mcp?.RegistrationId}, {actual.Mcp?.Description}, {actual.Mcp?.AnthropicApiKey}.");
    }

    [Fact(DisplayName = "TEST9: Reject an unknown setting inside the mcp section.")]
    public void Load_WithUnknownMcpSetting_Throws()
    {
        Assert.Throws<InvalidConfigurationException>(() => LabSettings.Load(FolderWith("{\"mcp\": {\"description\": \"Room\", \"apiKey\": \"k\"}}")));
    }
}
