using System.Text;
using P709.Core.Connection;

namespace P709.Core.Configuration;

/// <summary>Checks that a loaded <see cref="RoomConfiguration"/> is complete and consistent, so mistakes in the JSON surface at start-up rather than as a broken panel.</summary>
public static class RoomConfigValidator
{
    /// <summary>Throws <see cref="InvalidConfigurationException"/> listing every problem found.</summary>
    public static void Validate(RoomConfiguration config)
    {
        var problems = new List<string>();

        if (config.ConfigurationVersion < 1)
        {
            problems.Add("configurationVersion must be 1 or more.");
        }

        if (string.IsNullOrWhiteSpace(config.RoomName))
        {
            problems.Add("roomName is required.");
        }

        CheckSvg("roomSvg", config.RoomSvg, problems, true);

        // Values come from a file, so collections and objects can be absent at runtime whatever the annotations say.
        var sources = config.Sources ?? [];
        var lights = config.Lights ?? [];
        var lightingPresets = config.LightingPresets ?? [];
        var shades = config.Shades ?? [];
        var shadePresets = config.ShadePresets ?? [];

        CheckIds("sources", sources.Select(source => source.Id), problems);
        CheckIds("lights", lights.Select(light => light.Id), problems);
        CheckIds("lightingPresets", lightingPresets.Select(preset => preset.Id), problems);
        CheckIds("shades", shades.Select(shade => shade.Id), problems);
        CheckIds("shadePresets", shadePresets.Select(preset => preset.Id), problems);

        // Optional until the MCP lab, but when present it must be a usable path that does not clash with the room hub.
        if (config.McpHubPath is not null)
        {
            if (!config.McpHubPath.StartsWith('/') || config.McpHubPath.Any(char.IsWhiteSpace))
            {
                problems.Add($"mcpHubPath must be a URL path starting with /, was \"{config.McpHubPath}\".");
            }
            else if (string.Equals(config.McpHubPath.TrimEnd('/'), ConnectionDetails.HubPath, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"mcpHubPath must differ from the room hub path {ConnectionDetails.HubPath}.");
            }
        }

        foreach (var source in sources)
        {
            // The icon is optional for a source; when given it must be SVG.
            if (source.IconSvg is not null)
            {
                CheckSvg($"source \"{source.Id}\" iconSvg", source.IconSvg, problems);
            }
        }

        foreach (var light in lights)
        {
            CheckSvg($"light \"{light.Id}\" iconSvg", light.IconSvg, problems);

            var positions = light.Positions ?? [];

            if (positions.Count == 0)
            {
                problems.Add($"light \"{light.Id}\" needs at least one position.");
            }

            foreach (var position in positions)
            {
                if (position is null || position.XPercent is < 0 or > 100 || position.YPercent is < 0 or > 100)
                {
                    problems.Add($"light \"{light.Id}\" needs a position with x and y between 0 and 100.");
                }
            }
        }

        var lightIds = lights.Select(light => light.Id).ToHashSet();

        foreach (var preset in lightingPresets)
        {
            foreach (var level in preset.Levels ?? [])
            {
                if (!lightIds.Contains(level.LightId))
                {
                    problems.Add($"lighting preset \"{preset.Id}\" refers to unknown light \"{level.LightId}\".");
                }

                if (level.LevelPercent is < 0 or > 100)
                {
                    problems.Add($"lighting preset \"{preset.Id}\" level for light \"{level.LightId}\" must be between 0 and 100.");
                }
            }
        }

        var shadeIds = shades.Select(shade => shade.Id).ToHashSet();
        var tiltCapableShadeIds = shades.Where(shade => shade.SupportsTilt).Select(shade => shade.Id).ToHashSet();

        foreach (var preset in shadePresets)
        {
            foreach (var target in preset.Targets ?? [])
            {
                if (!shadeIds.Contains(target.ShadeId))
                {
                    problems.Add($"shade preset \"{preset.Id}\" refers to unknown shade \"{target.ShadeId}\".");
                    continue;
                }

                if (target.PositionPercent is < 0 or > 100)
                {
                    problems.Add($"shade preset \"{preset.Id}\" position for shade \"{target.ShadeId}\" must be between 0 and 100.");
                }

                if (target.TiltPercent is not null)
                {
                    if (!tiltCapableShadeIds.Contains(target.ShadeId))
                    {
                        problems.Add($"shade preset \"{preset.Id}\" sets a tilt for shade \"{target.ShadeId}\", which does not support tilt.");
                    }
                    else if (target.TiltPercent is < 0 or > 100)
                    {
                        problems.Add($"shade preset \"{preset.Id}\" tilt for shade \"{target.ShadeId}\" must be between 0 and 100.");
                    }
                }
            }
        }

        if (config.Concierge is not null)
        {
            var concierge = config.Concierge;

            if (string.IsNullOrWhiteSpace(concierge.BaseUrl) || !Uri.TryCreate(concierge.BaseUrl, UriKind.Absolute, out _))
            {
                problems.Add($"concierge baseUrl must be an absolute URL, was \"{concierge.BaseUrl}\".");
            }

            if (string.IsNullOrWhiteSpace(concierge.RoomId))
            {
                problems.Add("concierge roomId is required.");
            }

            if (string.IsNullOrWhiteSpace(concierge.Token))
            {
                problems.Add("concierge token is required.");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidConfigurationException("Invalid room configuration: " + string.Join(" ", problems));
        }
    }

    private static void CheckIds(string collection, IEnumerable<string> ids, List<string> problems)
    {
        var list = ids.ToList();

        if (list.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add($"every entry in {collection} needs an id.");
        }

        foreach (var duplicate in list.Where(id => !string.IsNullOrWhiteSpace(id)).GroupBy(id => id).Where(group => group.Count() > 1))
        {
            problems.Add($"{collection} has more than one entry with id \"{duplicate.Key}\".");
        }
    }

    // SVGs are stored and served as base64 (so the file can hold them without JSON-escaping issues and the
    // front-end always receives base64 it can drop straight into a data: URI); this decodes just far enough
    // to confirm the field really is base64-encoded inline SVG markup.
    // The optional parameter allows the caller to say that a missing value is acceptable (for example, the roomSvg is optional).
    private static void CheckSvg(string what, string? base64Svg, List<string> problems, bool optional=true)
    {
        if ( string.IsNullOrEmpty(base64Svg) && optional )
        {
            return;
        }

        string decoded;

        try
        {
            decoded = base64Svg is null ? "" : Encoding.UTF8.GetString(Convert.FromBase64String(base64Svg));
        }
        catch (FormatException)
        {
            problems.Add($"{what} must be base64-encoded inline SVG markup.");
            return;
        }

        if (base64Svg is null || !decoded.TrimStart().StartsWith("<svg", StringComparison.Ordinal))
        {
            problems.Add($"{what} must be base64-encoded inline SVG markup starting with <svg.");
        }
    }
}
