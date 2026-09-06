using System.Globalization;
using System.Text;
using Fabulis.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Fabulis.Cli.Tests;

/// <summary>
/// A canonical text rendering of everything the archive format is supposed to
/// preserve. Two vaults with equal snapshots round-tripped losslessly.
///
/// WHEN YOU ADD AN ENTITY OR A CONTENT-BEARING COLUMN, ADD IT HERE. That is
/// what makes RoundTripTests fail when the exporter forgets it.
///
/// Deliberately excluded, per the spec's "Known lossy fields":
///   row ids; Story.CreatedAt and Category.CreatedAt (derived on import);
///   story summaries (regenerated); OneLiner/Trope timestamps.
/// </summary>
internal static class VaultSnapshot
{
    public static async Task<string> CaptureAsync(FabulisDbContext db)
    {
        var sb = new StringBuilder();

        sb.AppendLine("== storytellers ==");
        var storytellers = await db.Storytellers
            .OrderBy(s => s.Name).AsNoTracking().ToListAsync();
        foreach (var s in storytellers)
        {
            sb.AppendLine($"name={s.Name}");
            sb.AppendLine($"  model={s.ModelName}");
            sb.AppendLine($"  temperature={Num(s.Temperature)}");
            sb.AppendLine($"  topP={Num(s.TopP)}");
            sb.AppendLine($"  maxTokens={Num(s.MaxTokens)}");
            sb.AppendLine($"  minP={Num(s.MinP)}");
            sb.AppendLine($"  topK={Num(s.TopK)}");
            sb.AppendLine($"  topA={Num(s.TopA)}");
            sb.AppendLine($"  reasoningEffort={s.ReasoningEffort?.ToString() ?? "-"}");
            sb.AppendLine($"  created={Stamp(s.CreatedAt)}");
            sb.AppendLine($"  prompt={Block(s.Prompt)}");
            sb.AppendLine($"  titlingPrompt={Block(s.TitlingPrompt)}");
        }

        sb.AppendLine("== library ==");
        var categories = await db.Categories
            .Include(c => c.Stories).ThenInclude(st => st.Versions).ThenInclude(v => v.Messages)
            .Include(c => c.Prompts).ThenInclude(p => p.Messages)
            .Include(c => c.OneLiners)
            .Include(c => c.Tropes)
            .OrderBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync();

        foreach (var category in categories)
        {
            sb.AppendLine($"category={category.Name}");

            foreach (var story in category.Stories.OrderBy(s => s.Title, StringComparer.Ordinal))
            {
                sb.AppendLine($"  story={story.Title}");
                foreach (var version in story.Versions.OrderBy(v => v.VersionNumber))
                {
                    sb.AppendLine($"    version={version.VersionNumber}");
                    sb.AppendLine($"      model={version.ModelName}");
                    sb.AppendLine($"      created={Stamp(version.CreatedAt)}");
                    foreach (var m in version.Messages.OrderBy(m => m.SortOrder))
                        sb.AppendLine($"      [{m.SortOrder}] {m.Role}: {Block(m.Content)}");
                }
            }

            foreach (var prompt in category.Prompts.OrderBy(p => p.Title, StringComparer.Ordinal))
            {
                sb.AppendLine($"  prompt={prompt.Title}");
                sb.AppendLine($"    created={Stamp(prompt.CreatedAt)}");
                sb.AppendLine($"    updated={Stamp(prompt.UpdatedAt)}");
                foreach (var m in prompt.Messages.OrderBy(m => m.SortOrder))
                    sb.AppendLine($"    [{m.SortOrder}] {Block(m.Content)}");
            }

            // Order matters: it is the display order the file preserves.
            foreach (var o in category.OneLiners.OrderBy(o => o.Id))
                sb.AppendLine($"  oneLiner={Block(o.Text)}");
            foreach (var t in category.Tropes.OrderBy(t => t.Id))
                sb.AppendLine($"  trope={Block(t.Text)}");
        }

        sb.AppendLine("== drafts ==");
        var drafts = await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages)
            .AsNoTracking()
            .ToListAsync();

        foreach (var draft in drafts
            .OrderBy(d => d.Storyteller.Name, StringComparer.Ordinal)
            .ThenBy(d => d.CreatedAt)
            .ThenBy(d => d.Title, StringComparer.Ordinal))
        {
            sb.AppendLine($"draft={draft.Title ?? "-"}");
            sb.AppendLine($"  storyteller={draft.Storyteller.Name}");
            sb.AppendLine($"  created={Stamp(draft.CreatedAt)}");
            sb.AppendLine($"  updated={Stamp(draft.UpdatedAt)}");
            foreach (var m in draft.Messages.OrderBy(m => m.SortOrder))
                sb.AppendLine($"  [{m.SortOrder}] {m.Role}: {Block(m.Content)}");
        }

        sb.AppendLine("== settings ==");
        var settings = await db.AppSettings.OrderBy(s => s.Key).AsNoTracking().ToListAsync();
        foreach (var setting in settings)
        {
            // Redacted by default, so it is not part of the round-trip claim.
            if (setting.Key == "OpenRouterApiKey") continue;
            sb.AppendLine($"{setting.Key}={Block(setting.Value)}");
        }

        return sb.ToString();
    }

    private static string Stamp(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);

    private static string Num(double? value) =>
        value?.ToString("R", CultureInfo.InvariantCulture) ?? "-";

    private static string Num(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    /// <summary>Renders multi-line text on one line so diffs stay readable.</summary>
    private static string Block(string text) =>
        text.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
}
