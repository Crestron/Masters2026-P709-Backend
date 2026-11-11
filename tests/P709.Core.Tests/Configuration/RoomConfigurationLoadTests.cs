using P709.Core.Configuration;

namespace P709.Core.Tests.Configuration;

public class RoomConfigurationLoadTests : IDisposable
{
    private const string ValidJson = """
        {
          "configurationVersion": 2,
          "roomName": "Board Room",
          "roomSvg": "PHN2Zy8+",
          "sources": [ { "id": "tv", "name": "TV", "type": "laptopUsbC", "iconSvg": "PHN2Zy8+" } ],
          "lights": [ { "id": "front", "name": "Front", "type": "dimmer", "iconSvg": "PHN2Zy8+", "positions": [ { "xPercent": 10.5, "yPercent": 20 } ] } ],
          "lightingPresets": [ { "id": "on", "name": "On", "levels": [ { "lightId": "front", "levelPercent": 75 } ] } ]
        }
        """;

    private readonly string _directory = Directory.CreateTempSubdirectory("p709-config-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FolderWith(string contents)
    {
        var path = Path.Combine(_directory, RoomConfiguration.FileName);
        File.WriteAllText(path, contents);

        return _directory;
    }

    [Fact(DisplayName = "TEST1: Load every part of the config from the file.")]
    public void Load_WithValidFile_ReturnsFileContents()
    {
        var config = RoomConfiguration.Load(FolderWith(ValidJson));

        Assert.True(2 == config.ConfigurationVersion, $"Version was {config.ConfigurationVersion}.");
        Assert.True("Board Room" == config.RoomName, $"Room name was {config.RoomName}.");
        Assert.True("PHN2Zy8+" == config.RoomSvg, "The room SVG should be loaded as base64, unchanged.");
        Assert.True(SourceType.LaptopUsbC == config.Sources[0].Type, "The \"laptopUsbC\" source type was not mapped.");
        Assert.True(new PositionConfig(10.5, 20) == config.Lights[0].Positions[0], "The light position was not loaded.");
        Assert.True(75 == config.LightingPresets[0].Levels[0].LevelPercent, "The preset level was not loaded.");
    }

    [Fact(DisplayName = "TEST2: A source can omit its icon, which loads as null.")]
    public void Load_WithSourceWithoutIcon_LoadsNullIcon()
    {
        var withoutIcon = ValidJson.Replace("\"type\": \"laptopUsbC\", \"iconSvg\": \"PHN2Zy8+\"", "\"type\": \"laptopUsbC\"");

        var config = RoomConfiguration.Load(FolderWith(withoutIcon));

        Assert.True(config.Sources[0].IconSvg is null, "A source without iconSvg should load with a null icon.");
    }

    [Fact(DisplayName = "TEST3: Fail with FileNotFoundException when the file is missing.")]
    public void Load_WithMissingFile_Throws()
    {
        var exception = Assert.Throws<FileNotFoundException>(() => RoomConfiguration.Load(_directory));

        Assert.True(exception.Message.Contains(RoomConfiguration.FileName), $"The message should name the file: {exception.Message}");
    }

    [Theory(DisplayName = "TEST4: Fail with InvalidConfigurationException for an empty or malformed file.")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ not json")]
    public void Load_WithUnusableJson_Throws(string contents)
    {
        Assert.Throws<InvalidConfigurationException>(() => RoomConfiguration.Load(FolderWith(contents)));
    }

    [Fact(DisplayName = "TEST5: Reject a misspelt property instead of silently ignoring it.")]
    public void Load_WithUnknownProperty_Throws()
    {
        var misspelt = ValidJson.Replace("\"roomName\"", "\"roomNmae\"");

        Assert.Throws<InvalidConfigurationException>(() => RoomConfiguration.Load(FolderWith(misspelt)));
    }

    [Fact(DisplayName = "TEST6: Reject an unknown source type.")]
    public void Load_WithUnknownSourceType_Throws()
    {
        var unknown = ValidJson.Replace("\"laptopUsbC\"", "\"toaster\"");

        Assert.Throws<InvalidConfigurationException>(() => RoomConfiguration.Load(FolderWith(unknown)));
    }

    [Fact(DisplayName = "TEST7: Reject a file that parses but is inconsistent.")]
    public void Load_WithPresetForUnknownLight_Throws()
    {
        var inconsistent = ValidJson.Replace("\"lightId\": \"front\"", "\"lightId\": \"nope\"");

        var exception = Assert.Throws<InvalidConfigurationException>(() => RoomConfiguration.Load(FolderWith(inconsistent)));

        Assert.True(exception.Message.Contains("nope"), $"The message should say what is wrong: {exception.Message}");
    }

    [Fact(DisplayName = "TEST8: Reject a file with whole sections missing.")]
    public void Load_WithMissingSections_Throws()
    {
        Assert.Throws<InvalidConfigurationException>(() => RoomConfiguration.Load(FolderWith("{ \"configurationVersion\": 1 }")));
    }

    [Fact(DisplayName = "TEST9: Load the concierge section when present.")]
    public void Load_WithConcierge_LoadsConcierge()
    {
        var withConcierge = ValidJson.Replace(
            "\"lightingPresets\"",
            "\"concierge\": { \"baseUrl\": \"http://192.168.200.115:5002\", \"roomId\": \"meeting-room-1\", \"token\": \"demo-concierge-token-2026\" }, \"lightingPresets\"");

        var config = RoomConfiguration.Load(FolderWith(withConcierge));

        Assert.True(config.Concierge is not null, "Expected a concierge configuration.");
        Assert.True("http://192.168.200.115:5002" == config.Concierge!.BaseUrl, $"concierge baseUrl was {config.Concierge.BaseUrl}.");
        Assert.True("meeting-room-1" == config.Concierge.RoomId, $"concierge roomId was {config.Concierge.RoomId}.");
        Assert.True("demo-concierge-token-2026" == config.Concierge.Token, $"concierge token was {config.Concierge.Token}.");
    }

    [Fact(DisplayName = "TEST10: A config without a concierge section loads with a null concierge.")]
    public void Load_WithoutConcierge_LoadsNullConcierge()
    {
        var config = RoomConfiguration.Load(FolderWith(ValidJson));

        Assert.True(config.Concierge is null, "A config without concierge should load with a null concierge.");
    }
}
