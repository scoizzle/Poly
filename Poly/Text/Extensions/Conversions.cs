namespace Poly;

public static partial class StringConversion {
    public static Stream GetStream(this string This, Encoding encoding) => new MemoryStream(encoding.GetBytes(This), false);

    public static IEnumerable<char> Escape(this string This, int offset, int count) {
        foreach (var character in This.Skip(offset).Take(count)) {
            switch (character) {
                case '\r':
                    yield return '\\';
                    yield return 'r';
                    break;

                case '\n':
                    yield return '\\';
                    yield return 'n';
                    break;

                case '\t':
                    yield return '\\';
                    yield return 't';
                    break;

                case '\f':
                    yield return '\\';
                    yield return 'f';
                    break;

                case '\b':
                    yield return '\\';
                    yield return 'b';
                    break;

                case '\"':
                    yield return '\\';
                    yield return '"';
                    break;

                case '\\':
                    yield return '\\';
                    yield return '\\';
                    break;

                default:
                    yield return character;
                    break;

            }
        }
    }

    public static IEnumerable<char> Descape(this string This, int offset, int count) {
        var enumerator = This.Skip(offset).Take(count).GetEnumerator();

        while (enumerator.MoveNext()) {
            var character = enumerator.Current;

            if (character == '\\' && enumerator.MoveNext()) {
                yield return character switch {
                    'r' => '\r',
                    'n' => '\n',
                    't' => '\t',
                    'f' => '\f',
                    'b' => '\b',
                    '"' => '"',
                    _ => character,
                };
            }

            yield return character;
        }
    }

    public static string Escape(this string This) =>
        string.Concat(This.Escape(0, This.Length));

    public static string Descape(this string This) =>
        string.Concat(This.Descape(0, This.Length));

    public static string HtmlEscape(this string This)
        => Uri.EscapeDataString(This);

    public static string UriEscape(this string This)
        => Uri.EscapeDataString(This);

    [GeneratedRegex("%([A-Fa-f\\d]{2})")]
    private static partial Regex HexCodeRegex();

    public static string HtmlDescape(this string This)
        => HexCodeRegex().Replace(This.Replace("+", " "), a => Convert.ToChar(Convert.ToInt32(a.Groups[1].Value, 16)).ToString());

    public static string UriDescape(this string This)
        => Uri.UnescapeDataString(This);

    public static string Base64Encode(this string This, Encoding encoding)
        => Convert.ToBase64String(encoding.GetBytes(This));

    public static string Base64Decode(this string This, Encoding encoding)
        => encoding.GetString(Convert.FromBase64String(This));
}