namespace P709.Web.Mcp;

/// <summary>
/// The room's MCP Gateway registration state, as text (for example <c>Registered</c>), so the panel can show whether the AI
/// can reach the room. The program sets it as the gateway connection changes; <see cref="McpHub"/> passes changes on.
/// </summary>
public sealed class McpStatus
{
    /// <summary>The value until the program reports a registration state: MCP has not been started.</summary>
    public const string NotStarted = "NotStarted";

    /// <summary>The current registration state.</summary>
    public string Value { get; private set; } = NotStarted;

    /// <summary>Raised with the new value whenever it changes.</summary>
    public event Action<string>? Changed;

    /// <summary>Sets <see cref="Value"/> and raises <see cref="Changed"/> if it differs.</summary>
    public void Set(string value)
    {
        if (value == Value)
        {
            return;
        }

        Value = value;
        Changed?.Invoke(value);
    }
}
