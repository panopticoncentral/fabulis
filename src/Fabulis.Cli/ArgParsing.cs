namespace Fabulis.Cli;

/// <summary>
/// Splits CLI arguments into flags (anything starting with "--") and
/// positionals (everything else), and holds the fixed set of flags this CLI
/// recognizes. Lives in its own file — rather than as a local function
/// inside <c>Program.cs</c>'s top-level statements, which allow local
/// functions but not field declarations — so <see cref="KnownFlags"/> can be
/// a real field and so the parsing is unit-testable without exercising the
/// console entry point (password prompt, vault open, ...).
/// </summary>
public static class ArgParsing
{
    public static readonly IReadOnlyList<string> KnownFlags = ["--mirror", "--include-secrets", "--yes"];

    /// <summary>
    /// Which flags each verb actually does something with. A flag that is
    /// globally known (<see cref="KnownFlags"/>) but absent from a verb's own
    /// list here is accepted-but-inert on that verb today unless callers also
    /// check <see cref="InvalidFlagsForVerb"/> — e.g. <c>--mirror</c> only
    /// affects <c>import</c>; passing it to <c>export</c> or <c>sillytavern</c>
    /// would otherwise be silently ignored.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> FlagsByVerb =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["export"] = ["--include-secrets"],
            ["import"] = ["--mirror", "--yes"],
            ["sillytavern"] = [],
        };

    public static (List<string> Positionals, HashSet<string> Flags) Parse(string[] args)
    {
        var positionals = new List<string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal)) flags.Add(arg);
            else positionals.Add(arg);
        }

        return (positionals, flags);
    }

    /// <summary>
    /// Flags present that this verb does not accept, in the order given by
    /// <paramref name="flags"/>. A verb not present in <see cref="FlagsByVerb"/>
    /// (an unrecognized verb, handled elsewhere) is treated as accepting no
    /// flags at all, so every flag on it comes back as invalid.
    /// </summary>
    public static List<string> InvalidFlagsForVerb(string verb, IEnumerable<string> flags)
    {
        var allowed = FlagsByVerb.TryGetValue(verb, out var list) ? list : [];
        return flags.Where(f => !allowed.Contains(f)).ToList();
    }
}
