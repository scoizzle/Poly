using Poly.DomainModeling.Language;

namespace Poly.Tests.Grammar;

/// <summary>
/// Characterisation pins (G-27 P2) for the hand reader of the <c>uses</c> pin,
/// <see cref="DslSyntaxTree.TryReadUse"/>: the first pin token is on the id's line, each later token is
/// adjacent to the one before it, and every token is an identifier, number, string literal, minus or dot.
/// <see cref="Cases"/> is shared with <c>GrammarRunTests</c>, whose same-line Run must consume exactly
/// the tokens this reader consumes.
/// </summary>
public sealed class UsesPinReadPins {
    /// <summary>An input, the pin <see cref="DslSyntaxTree.TryReadUse"/> reads (null for none), and the token count it consumes after the id.</summary>
    internal static readonly (string Source, string? Version, int PinTokens)[] Cases = [
        ("uses http v1", "v1", 1),
        ("uses http 1.2.3", "1.2.3", 3),
        ("uses http \"v1\"", "v1", 1),
        ("uses http -2", "-2", 2),
        ("uses http\nv1", null, 0),
        ("uses http v1 .2", "v1", 1),
        ("uses http", null, 0),
        ("uses http v1-beta", "v1-beta", 3),
        ("uses http 2.0\nuses storage", "2.0", 1),
        ("uses http v1.2\nEntity: entity {}", "v1.2", 3),
        ("uses http ( v1 )", null, 0),
        ("uses http v1 v2", "v1", 1),
    ];

    internal static List<DslToken> Tokens(string source) {
        var reader = new DslTokenReader(source);
        var tokens = new List<DslToken>();
        for (var i = 0; ; i++) {
            var token = reader.Peek(i);
            tokens.Add(token);
            if (reader.EndOfStream(token.Kind))
                return tokens;
        }
    }

    internal static string Shown(string text) => text.Replace("\n", "\\n", StringComparison.Ordinal);

    [Test]
    public async Task TryReadUse_EachCase_ReadsItsPinAndConsumesItsTokens() {
        var expected = Cases.Select(c => $"{Shown(c.Source)} => True http {c.Version ?? "(none)"} next={2 + c.PinTokens}");
        var actual = Cases.Select(c => {
            var read = DslSyntaxTree.TryReadUse(Tokens(c.Source), 0, out var id, out var pin, out var next);
            return $"{Shown(c.Source)} => {read} {id} {pin ?? "(none)"} next={next}";
        });

        await Assert.That(string.Join("\n", actual)).IsEqualTo(string.Join("\n", expected));
    }

    [Test]
    public async Task Cases_IncludeAMultiTokenPinAndAStop() {
        await Assert.That(Cases.Length).IsEqualTo(12);
        await Assert.That(Cases.Count(c => c.PinTokens > 1)).IsGreaterThanOrEqualTo(3);
        await Assert.That(Cases.Count(c => c.PinTokens == 0)).IsGreaterThanOrEqualTo(3);
    }
}