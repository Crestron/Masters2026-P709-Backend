using Newtonsoft.Json;

namespace P709.Core.Connection;

/// <summary>
/// The optional <c>mcp</c> section of <see cref="LabSettings"/>: how this room registers with the MCP Gateway, and the key for
/// the AI troubleshooting conversation. It lives in the settings file, not the room configuration, because the room
/// configuration is sent to the panel as it is and the API key must never reach it.
/// </summary>
public sealed class McpSettings
{
    /// <summary>The value shipped in the settings file. While the key is still this, troubleshooting is unavailable.</summary>
    public const string ApiKeyPlaceholder = "YOUR-ANTHROPIC-API-KEY";

    /// <summary>A number chosen for this room, unique within the program. The gateway knows the room by it and the program number.</summary>
    [JsonProperty("registrationId")]
    public int RegistrationId { get; init; } = 1;

    /// <summary>A short summary of the room for the AI: what it has and what it is used for.</summary>
    [JsonProperty("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Anthropic API key for the troubleshooting conversation. Optional: the MCP tools work without it.</summary>
    [JsonProperty("anthropicApiKey")]
    public string? AnthropicApiKey { get; init; }

    /// <summary>Whether <see cref="AnthropicApiKey"/> has been set to something other than blank or the placeholder.</summary>
    [JsonIgnore]
    public bool IsApiKeyConfigured => !string.IsNullOrWhiteSpace(AnthropicApiKey) && AnthropicApiKey != ApiKeyPlaceholder;
}
