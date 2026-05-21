using System.Text;
using Fabulis.Server.Data;

namespace Fabulis.Cli;

/// <summary>
/// Single source of truth for the on-disk draft markdown shape used by
/// the export and sillytavern verbs.
/// </summary>
internal static class DraftMarkdownWriter
{
    public static string FormatDraft(
        string storytellerName,
        string modelName,
        DateTime createdUtc,
        DateTime updatedUtc,
        IEnumerable<(MessageRole Role, string Content, int SortOrder)> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Storyteller: {storytellerName}");
        sb.AppendLine($"Model: {modelName}");
        sb.AppendLine($"Created: {createdUtc:O}");
        sb.AppendLine($"Updated: {updatedUtc:O}");
        sb.AppendLine();
        sb.Append(FormatConversation(messages));
        return sb.ToString();
    }

    public static string FormatConversation(
        IEnumerable<(MessageRole Role, string Content, int SortOrder)> messages)
    {
        var ordered = messages.OrderBy(m => m.SortOrder).ToList();
        var sb = new StringBuilder();

        foreach (var message in ordered)
        {
            var label = message.Role switch
            {
                MessageRole.Prompt => "**Me:**",
                MessageRole.Response => "**StoryTeller:**",
                _ => throw new InvalidOperationException($"Unknown role: {message.Role}")
            };

            sb.AppendLine(label);
            sb.AppendLine();
            sb.AppendLine(message.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
