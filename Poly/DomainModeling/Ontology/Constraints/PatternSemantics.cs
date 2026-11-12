using System.Text.RegularExpressions;

namespace Poly.DomainModeling.Ontology.Constraints;

/// <summary>
/// The one place that evaluates a <c>pattern(...)</c> regex and the one place that
/// decides what its end anchor means. A trailing unescaped <c>$</c> is absolute end
/// of input (<c>\z</c>) here, in the guard L-20 prints, in analysis, in the
/// constant-assign fold and at the contract boundary check, so no two of them can
/// disagree. The model keeps the pattern exactly as authored, so DSL, JSON Schema
/// and SHACL export keep their own dialect's meaning.
/// </summary>
internal static class PatternSemantics {
    /// <summary>
    /// Anchors a pattern at the absolute end of the input. .NET's <c>$</c> also
    /// matches before a final newline, so an author's unescaped trailing <c>$</c> is
    /// rewritten to <c>\z</c>. A pattern that does not end in an unescaped <c>$</c> —
    /// no end anchor, or an escaped literal <c>\$</c> — keeps its authored meaning and
    /// is returned unchanged. One syntactic step.
    /// </summary>
    internal static string EndAnchored(string pattern) {
        if (pattern.Length == 0 || pattern[^1] != '$')
            return pattern;
        var backslashes = 0;
        for (var i = pattern.Length - 2; i >= 0 && pattern[i] == '\\'; i--)
            backslashes++;
        // An odd backslash run escapes the '$' (a literal dollar), not an anchor.
        return backslashes % 2 == 1 ? pattern : pattern[..^1] + "\\z";
    }

    /// <summary>
    /// True when <paramref name="value"/> matches <paramref name="pattern"/> under
    /// <see cref="EndAnchored"/>'s meaning. An unparsable pattern matches nothing
    /// instead of throwing; the caller's own validity diagnostic already reported it.
    /// </summary>
    internal static bool TryMatch(string value, string pattern) {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pattern);
        try {
            return Regex.IsMatch(value, EndAnchored(pattern));
        }
        catch (RegexParseException) {
            return false;
        }
    }
}