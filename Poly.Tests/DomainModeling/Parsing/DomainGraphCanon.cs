using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

using Poly.Ast;

namespace Poly.Tests.DomainModeling.Parsing;

/// <summary>
/// A structural canonical form of a <see cref="Poly.DomainModeling.Ontology.Domain"/> graph for the round-trip matrix.
/// It reads EVERY instance property of every record it meets (public and internal, so <c>Entity.KeyProperties</c> and
/// <c>InitialStageName</c> are seen) by reflection, so a new model attribute is compared the day it exists.
///
/// <para><b>Termination.</b> The walk keeps the chain of objects on the current path and throws on a revisit
/// (a cycle), and throws past depth <see cref="MaxDepth"/>; neither happens on a <c>Domain</c>, whose records
/// are an owned tree (a navigation names its target by <c>DomainTypeReference</c>, not by object).
/// <c>CanonicalTests</c> proves both limits.</para>
///
/// <para><b>What it leaves out, and why (<see cref="Excluded"/>).</b> <c>Id</c> is minted per run;
/// <c>Children</c> is a computed view of members already walked; <c>EqualityContract</c> is compiler plumbing;
/// <c>MemberOrder</c> is the authored interleaving of members, which a code-built entity does not carry (the printer then
/// falls back to the kind order, and the matrix compares the printed text, which shows member order);
/// <c>Line</c> and <c>Col</c> are source positions, which only a parsed node carries.</para>
/// </summary>
internal static class DomainGraphCanon {
    internal const int MaxDepth = 80;

    /// <summary>Property names left out of the comparison, with the reason (see the class remarks).</summary>
    internal static readonly IReadOnlyDictionary<string, string> Excluded = new Dictionary<string, string>(StringComparer.Ordinal) {
        ["Id"] = "a NodeId is minted per run",
        ["Children"] = "a computed view of members the walk already visits",
        ["EqualityContract"] = "compiler-generated record plumbing",
        ["MemberOrder"] = "the authored member interleaving; a built entity carries none, and the printed text shows member order",
        ["Line"] = "a source position, carried only by a parsed node",
        ["Col"] = "a source position, carried only by a parsed node",
    };

    private static readonly Dictionary<Type, PropertyInfo[]> Cache = [];

    internal static string Of(object? value) {
        var sb = new StringBuilder();
        Write(value, sb, 0, []);
        return sb.ToString();
    }

    private static void Write(object? value, StringBuilder sb, int depth, List<object> path) {
        if (depth > MaxDepth)
            throw new InvalidOperationException($"The canonical walk is deeper than {MaxDepth} levels.");
        switch (value) {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append('"').Append(s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
                return;
            case bool b:
                sb.Append(b ? "true" : "false");
                return;
            case Enum e:
                sb.Append(e.GetType().Name).Append('.').Append(e);
                return;
            case Type t:
                sb.Append("typeof(").Append(t.Name).Append(')');
                return;
            case IFormattable f when value.GetType().IsPrimitive || value is decimal:
                sb.Append(value.GetType().Name).Append(':').Append(f.ToString(null, CultureInfo.InvariantCulture));
                return;
            case IDictionary dictionary: {
                    var entries = new List<string>();
                    foreach (DictionaryEntry entry in dictionary)
                        entries.Add(Of(entry.Key) + "=" + Of(entry.Value));
                    entries.Sort(StringComparer.Ordinal);
                    sb.Append('{').Append(string.Join(",", entries)).Append('}');
                    return;
                }
            case IEnumerable list: {
                    sb.Append('[');
                    var first = true;
                    foreach (var item in list) {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(item, sb, depth + 1, path);
                    }

                    sb.Append(']');
                    return;
                }
        }

        var type = value.GetType();
        if (path.Any(p => ReferenceEquals(p, value)))
            throw new InvalidOperationException($"The canonical walk met a {type.Name} that contains itself (a cycle).");
        path.Add(value);
        sb.Append(type.Name).Append('(');
        var props = PropertiesOf(type);
        for (var i = 0; i < props.Length; i++) {
            if (i > 0) sb.Append(',');
            sb.Append(props[i].Name).Append('=');
            Write(props[i].GetValue(value), sb, depth + 1, path);
        }

        sb.Append(')');
        path.RemoveAt(path.Count - 1);
    }

    /// <summary>Every instance property of the type that is compared (public and non-public; not indexers, not <see cref="Excluded"/>).</summary>
    internal static PropertyInfo[] PropertiesOf(Type type) {
        lock (Cache) {
            if (!Cache.TryGetValue(type, out var props)) {
                props = [.. type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(p => p.GetIndexParameters().Length == 0 && !Excluded.ContainsKey(p.Name) && p.PropertyType != typeof(NodeId))
                    .OrderBy(p => p.Name, StringComparer.Ordinal)];
                Cache[type] = props;
            }

            return props;
        }
    }
}