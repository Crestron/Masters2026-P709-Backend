using Newtonsoft.Json;

namespace P709.Core.Configuration;

/// <summary>
/// Reads the program's JSON files (settings and room configuration) one way: strictly, from a path, failing at start-up
/// with a message that names the file. Each caller then validates what it loaded.
/// </summary>
public static class JsonConfigFile
{
    private static readonly JsonSerializerSettings _settings = new()
    {
        // A typo in a property name is an error, not a silently missing setting.
        MissingMemberHandling = MissingMemberHandling.Error
    };

    /// <summary>Reads and deserialises the file.</summary>
    /// <param name="path">Full path of the JSON file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidConfigurationException">The file is empty, malformed or names an unknown property.</exception>
    public static T Load<T>(string path) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Configuration file not found: {path}", path);
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path), _settings)
                ?? throw new InvalidConfigurationException($"{path} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidConfigurationException($"{path} could not be read: {exception.Message}", exception);
        }
    }
}
