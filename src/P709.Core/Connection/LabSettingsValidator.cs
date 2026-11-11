using P709.Core.Configuration;

namespace P709.Core.Connection;

/// <summary>Checks that loaded <see cref="LabSettings"/> are usable, so a mistake in the settings file surfaces at start-up rather than as a web host that cannot start.</summary>
public static class LabSettingsValidator
{
    /// <summary>Throws <see cref="InvalidConfigurationException"/> listing every problem found.</summary>
    public static void Validate(LabSettings settings)
    {
        var problems = new List<string>();

        if (settings.ApiPort is < 1 or > 65535)
        {
            problems.Add($"apiPort must be between 1 and 65535, was {settings.ApiPort}.");
        }

        if (settings.Mcp is { } mcp)
        {
            if (mcp.RegistrationId < 1)
            {
                problems.Add($"mcp registrationId must be 1 or more, was {mcp.RegistrationId}.");
            }

            if (string.IsNullOrWhiteSpace(mcp.Description))
            {
                problems.Add("mcp description must not be blank.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidConfigurationException($"Invalid {LabSettings.FileName}: " + string.Join(" ", problems));
        }
    }
}
