using Newtonsoft.Json;

namespace P709.Web.Mcp;

/// <summary>
/// Everything the panel shows for the room's troubleshooting conversation, sent whole by <see cref="McpHub"/> on every change.
/// </summary>
/// <remarks>Provisional: this shape is still to be matched to the Svelte troubleshooting UI.</remarks>
/// <param name="IsAvailable">False when no Anthropic API key is configured (or MCP is off): Start does nothing.</param>
/// <param name="IsBusy">Whether Claude is working on a turn. Start and Answer are ignored while it is.</param>
/// <param name="Status">A status line: progress, the answer just chosen, why troubleshooting is unavailable, or why it failed.</param>
/// <param name="Message">Claude's latest message. Empty while Claude works on the next one.</param>
/// <param name="AnswerOptions">The answers the user can pick from, in order. <see cref="McpHub.AnswerTroubleshooting"/> takes an index into this.</param>
/// <param name="SelectedAnswerIndex">The answer just chosen, or null.</param>
/// <param name="IsDone">Whether Claude considers the conversation finished.</param>
public sealed record TroubleshootingState(
    [property: JsonProperty("isAvailable")] bool IsAvailable,
    [property: JsonProperty("isBusy")] bool IsBusy,
    [property: JsonProperty("status")] string Status,
    [property: JsonProperty("message")] string Message,
    [property: JsonProperty("answerOptions")] IReadOnlyList<string> AnswerOptions,
    [property: JsonProperty("selectedAnswerIndex")] int? SelectedAnswerIndex,
    [property: JsonProperty("isDone")] bool IsDone);
