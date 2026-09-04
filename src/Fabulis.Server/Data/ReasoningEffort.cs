using System.Text.Json.Serialization;

namespace Fabulis.Server.Data;

/// <summary>
/// How much chain-of-thought a reasoning model should spend on a call.
/// A null <c>ReasoningEffort?</c> means "send nothing and let the model
/// decide"; <see cref="Off"/> explicitly disables reasoning.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReasoningEffort>))]
public enum ReasoningEffort
{
    Off,
    Minimal,
    Low,
    Medium,
    High
}
