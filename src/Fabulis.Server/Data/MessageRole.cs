using System.Text.Json.Serialization;

namespace Fabulis.Server.Data;

[JsonConverter(typeof(JsonStringEnumConverter<MessageRole>))]
public enum MessageRole
{
    Prompt,
    Response
}
