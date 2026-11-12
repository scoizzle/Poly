using System.Globalization;
using System.Text;

namespace Poly.Runtime;

/// <summary>
/// The canonical text form of one storage key part and of the whole key. Every
/// <see cref="IStoragePort"/> and every generated host builds its keys here, so the
/// simulator and printed C# cannot drift.
/// </summary>
/// <remarks>
/// <para>
/// Each part is encoded as its invariant length, <c>':'</c>, then its text; a null part
/// is <c>'~'</c>. The encoding is injective: a part may contain <c>':'</c>, <c>'~'</c> or
/// a former separator without colliding with a different part list, because the length
/// prefix decides where the text ends.
/// </para>
/// <para>
/// "Canonical" here is text-canonical only: <c>1.5m</c> and <c>1.50m</c> encode
/// differently. Normalising a value's text is the caller's job (L-11); the codec only
/// guarantees one culture and one shape.
/// </para>
/// </remarks>
public static class KeyCodec {
    /// <summary>Encodes the parts as one canonical key.</summary>
    /// <param name="parts">The key parts; string, <see cref="IFormattable"/>, bool or char.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parts"/> is null.</exception>
    /// <exception cref="ArgumentException">A part is not one of the accepted types.</exception>
    public static string Canonical(params object?[] parts) {
        ArgumentNullException.ThrowIfNull(parts);
        var builder = new StringBuilder();
        foreach (var part in parts)
            AppendPart(builder, part);

        return builder.ToString();
    }

    /// <summary>Encodes the entity, property and value triple. Agrees with the array form.</summary>
    public static string Canonical(string entity, string property, string value) =>
        Canonical([(object?)entity, property, value]);

    private static void AppendPart(StringBuilder builder, object? part) {
        if (part is null) {
            builder.Append('~');
            return;
        }

        var text = Text(part);
        builder.Append(text.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':');
        builder.Append(text);
    }

    private static string Text(object part) => part switch {
        string text => text,
        char character => character.ToString(),
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => throw new ArgumentException(
            $"A storage key part must be a string, IFormattable, bool or char, not {part.GetType().FullName}.",
            nameof(part))
    };
}