using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Footnotes;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Fabulis.Server.Data;

/// <summary>
/// Converts the markdown stored in story / draft messages into plain
/// text suitable for TTS synthesis. Removes emphasis/heading/link
/// syntax (keeping link text), drops fenced code blocks entirely
/// (they sound awful read aloud), strips HTML tags but keeps their
/// inner text, drops footnote references, and collapses whitespace.
/// </summary>
public static class MarkdownStripper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseFootnotes()
        .Build();

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);
    // Match only tag-shaped patterns (open or close tags starting with a letter).
    // This avoids stripping prose like "if x < 3 then y > 2".
    private static readonly Regex HtmlTag = new(@"</?[A-Za-z][^>]*>", RegexOptions.Compiled);
    // Strips unresolved footnote reference markers like [^1] that Markdig
    // leaves as raw LiteralInline text when there is no matching definition.
    private static readonly Regex FootnoteRef = new(@"\[\^[^\]]+\]", RegexOptions.Compiled);

    public static string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;

        var doc = Markdown.Parse(markdown, Pipeline);
        var sb = new StringBuilder();
        Render(doc, sb);

        var withoutHtml = HtmlTag.Replace(sb.ToString(), string.Empty);
        var withoutFootnotes = FootnoteRef.Replace(withoutHtml, string.Empty);
        return WhitespaceRun.Replace(withoutFootnotes, " ").Trim();
    }

    private static void Render(MarkdownObject node, StringBuilder sb)
    {
        switch (node)
        {
            case FencedCodeBlock:
                return; // Drop entirely.
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case LiteralInline literal:
                sb.Append(literal.Content.ToString());
                break;
            case LineBreakInline:
                sb.Append(' ');
                break;
            case LinkInline link:
                foreach (var child in link) Render(child, sb);
                break;
            case FootnoteLink:
                return; // Drop footnote reference markers like [^1].
            case ContainerBlock container:
                foreach (var child in container) Render(child, sb);
                sb.Append(' ');
                break;
            case LeafBlock leaf when leaf.Inline is not null:
                foreach (var child in leaf.Inline) Render(child, sb);
                sb.Append(' ');
                break;
            case ContainerInline inline:
                foreach (var child in inline) Render(child, sb);
                break;
        }
    }
}
