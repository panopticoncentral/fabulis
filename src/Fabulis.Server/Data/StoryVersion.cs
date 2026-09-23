using System.Text.Json.Serialization;

namespace Fabulis.Server.Data;

[JsonConverter(typeof(JsonStringEnumConverter<StoryOrigin>))]
public enum StoryOrigin
{
    Generated,
    Imported,
}

public class StoryVersion
{
    public int Id { get; set; }
    public int StoryId { get; set; }
    public int VersionNumber { get; set; }
    public StoryOrigin Origin { get; set; } = StoryOrigin.Generated;
    public string? ModelName { get; set; }
    public DateTime CreatedAt { get; set; }

    public Story Story { get; set; } = null!;
    public List<StoryMessage> Messages { get; set; } = [];
}
