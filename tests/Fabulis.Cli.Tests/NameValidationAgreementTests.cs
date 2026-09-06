using Fabulis.Cli.Archive;
using Fabulis.Server.Api;
using Xunit;

namespace Fabulis.Cli.Tests;

/// <summary>
/// Pins the agreement between Fabulis.Server.Api.NameValidation (what the
/// server accepts as a category/story/prompt/storyteller name) and
/// Fabulis.Cli.Archive.ArchiveLayout.Sanitize (what the CLI writes to disk
/// on export). Task 9 exists so that a name the server accepts survives
/// export unchanged; if the two ever disagree, an accepted name can still
/// be silently rewritten on export, desyncing the database row from its
/// on-disk identity.
///
/// This test lives in Fabulis.Cli.Tests, not Fabulis.Server.Tests, because
/// NameValidation lives in Fabulis.Server and Fabulis.Server cannot
/// reference Fabulis.Cli (the CLI depends on the server for entity types,
/// not the other way around). Fabulis.Cli.Tests references Fabulis.Cli,
/// which project-references Fabulis.Server, so it is the only test project
/// that can see both types and check them against each other directly.
/// The two rules are necessarily duplicated (once as a validator, once as
/// a sanitizer) and could drift apart without a test like this one.
/// </summary>
public class NameValidationAgreementTests
{
    public static readonly TheoryData<string?> Corpus = new()
    {
        // Ordinary names.
        "Fables",
        "A Night In The Fens",
        "café stories",
        "dashes-and_underscores",
        "-Leading dash kept",

        // Names Sanitize would rewrite — must be rejected by validation.
        "My Story.",
        "Foo-",
        "...",
        "-",
        "  padded  ",

        // Unsafe or empty names — already rejected before this fix.
        "with/slash",
        "with\\backslash",
        "",
        "   ",
        ".",
        "..",
        "a" + (char)1 + "b",

        // Whitespace variants Sanitize treats specially via char.IsWhiteSpace.
        "Trailing NBSP\u00A0",
        "\u00A0Leading NBSP",
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void AcceptedNamesSurviveSanitizeUnchanged(string? name)
    {
        // One-directional on purpose: Sanitize is also a safety net for rows
        // already in the database that predate validation, so it legitimately
        // rewrites plenty of names validation rejects. We only assert that
        // nothing which passes validation is then changed by Sanitize.
        if (NameValidation.IsValidPathSegment(name))
        {
            Assert.Equal(name, ArchiveLayout.Sanitize(name!));
        }
    }
}
