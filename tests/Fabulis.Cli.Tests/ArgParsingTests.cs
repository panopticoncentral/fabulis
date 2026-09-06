using Xunit;

namespace Fabulis.Cli.Tests;

public class ArgParsingTests
{
    [Fact]
    public void SeparatesFlagsFromPositionals()
    {
        var (positionals, flags) = ArgParsing.Parse(["export", "/tmp/x", "--include-secrets"]);

        Assert.Equal(["export", "/tmp/x"], positionals);
        Assert.Equal(new HashSet<string> { "--include-secrets" }, flags);
    }

    [Fact]
    public void CollectsMultipleFlagsRegardlessOfPosition()
    {
        var (positionals, flags) = ArgParsing.Parse(["import", "--mirror", "/tmp/x", "--yes"]);

        Assert.Equal(["import", "/tmp/x"], positionals);
        Assert.Equal(new HashSet<string> { "--mirror", "--yes" }, flags);
    }

    [Fact]
    public void DeduplicatesRepeatedFlags()
    {
        var (_, flags) = ArgParsing.Parse(["import", "--yes", "--yes"]);

        Assert.Equal(new HashSet<string> { "--yes" }, flags);
    }

    [Fact]
    public void EmptyArgsProduceNoPositionalsOrFlags()
    {
        var (positionals, flags) = ArgParsing.Parse([]);

        Assert.Empty(positionals);
        Assert.Empty(flags);
    }

    [Fact]
    public void AllArgsPositionalWhenNoFlagsPresent()
    {
        var (positionals, flags) = ArgParsing.Parse(["sillytavern", "/tmp/src", "/tmp/dst"]);

        Assert.Equal(["sillytavern", "/tmp/src", "/tmp/dst"], positionals);
        Assert.Empty(flags);
    }

    [Theory]
    [InlineData("--mirror")]
    [InlineData("--include-secrets")]
    [InlineData("--yes")]
    public void RecognizesEachKnownFlag(string flag)
    {
        Assert.Contains(flag, ArgParsing.KnownFlags);
    }

    [Fact]
    public void UnknownFlagIsNotInKnownFlags()
    {
        var (_, flags) = ArgParsing.Parse(["export", "/tmp/x", "--bogus-flag"]);

        var unknown = flags.Except(ArgParsing.KnownFlags).ToList();

        Assert.Equal(["--bogus-flag"], unknown);
    }

    [Fact]
    public void EmptyStringArgumentIsPositionalNotFlag()
    {
        var (positionals, flags) = ArgParsing.Parse(["export", ""]);

        Assert.Equal(["export", ""], positionals);
        Assert.Empty(flags);
    }

    // --- Per-verb flag validation (Finding 1) ---

    [Theory]
    [InlineData("export", "--include-secrets")]
    [InlineData("import", "--mirror")]
    [InlineData("import", "--yes")]
    public void VerbAcceptsItsOwnFlagsWithoutError(string verb, string flag)
    {
        var invalid = ArgParsing.InvalidFlagsForVerb(verb, [flag]);

        Assert.Empty(invalid);
    }

    [Fact]
    public void ImportAcceptsBothOfItsFlagsTogether()
    {
        var invalid = ArgParsing.InvalidFlagsForVerb("import", ["--mirror", "--yes"]);

        Assert.Empty(invalid);
    }

    [Fact]
    public void SillytavernAcceptsNoFlags()
    {
        Assert.Empty(ArgParsing.FlagsByVerb["sillytavern"]);
    }

    [Fact]
    public void MirrorOnExportIsRejected()
    {
        var invalid = ArgParsing.InvalidFlagsForVerb("export", ["--mirror"]);

        Assert.Equal(["--mirror"], invalid);
    }

    [Fact]
    public void IncludeSecretsOnImportIsRejected()
    {
        var invalid = ArgParsing.InvalidFlagsForVerb("import", ["--include-secrets"]);

        Assert.Equal(["--include-secrets"], invalid);
    }

    [Theory]
    [InlineData("--yes")]
    [InlineData("--mirror")]
    [InlineData("--include-secrets")]
    public void AnyFlagOnSillytavernIsRejected(string flag)
    {
        var invalid = ArgParsing.InvalidFlagsForVerb("sillytavern", [flag]);

        Assert.Equal([flag], invalid);
    }

    [Fact]
    public void AllThreeFlagsOnSillytavernAreAllRejected()
    {
        var invalid = ArgParsing.InvalidFlagsForVerb("sillytavern", ["--yes", "--mirror", "--include-secrets"]);

        Assert.Equal(3, invalid.Count);
    }

    [Fact]
    public void UnknownFlagIsStillRejectedRegardlessOfVerb()
    {
        // A genuinely unknown flag is caught by the existing KnownFlags/Except
        // check in Program.cs before per-verb validation ever runs. Confirm
        // per-verb validation also flags it (defense in depth: an unknown
        // flag is not valid for any verb either).
        var invalid = ArgParsing.InvalidFlagsForVerb("export", ["--bogus-flag"]);

        Assert.Equal(["--bogus-flag"], invalid);
    }

    [Fact]
    public void MixOfValidAndInvalidFlagsReportsOnlyTheInvalidOnes()
    {
        var invalid = ArgParsing.InvalidFlagsForVerb("export", ["--include-secrets", "--mirror"]);

        Assert.Equal(["--mirror"], invalid);
    }
}
