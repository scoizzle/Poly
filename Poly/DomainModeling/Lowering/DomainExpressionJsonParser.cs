using System.Text.Json;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Direct JSON → <see cref="DomainExpression"/> parser. Produces
/// <see cref="DomainExpression"/> nodes directly from JSON strings with no
/// intermediate record or custom converters.
///
/// <para>All <see cref="JsonElement"/> values are normalized to proper CLR types
/// (int/long/bool/string/null) during parsing, so the resulting expression tree
/// is safe for the VM's <c>TryValueToLong</c> and ring-slot ABI.</para>
///
/// <h3>Supported shapes</h3>
/// <code>
/// // Comparison
/// {"property":"Age", "op":">=", "value":18}
///
/// // Composite
/// {"and": [{"property":"A","op":">=","value":1}, {"property":"B","op":"&lt;","value":5}]}
/// {"or":  [left, right]}
/// {"not": operand}
///
/// // Literal
/// {"literal": true}
///
/// // Relationship navigation (path-prefix)
/// {"relationship":"profile", "inner":{"property":"City","op":"==","value":"Metropolis"}}
/// </code>
/// </summary>
public static class DomainExpressionJsonParser {
    /// <summary>
    /// Parses a JSON expression string into a <see cref="DomainExpression"/>.
    /// </summary>
    /// <exception cref="ArgumentException">Invalid or malformed JSON expression.</exception>
    public static DomainExpression ParseJson(string json) {
        ArgumentNullException.ThrowIfNull(json);
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Expression JSON must not be empty.");

        using var doc = JsonDocument.Parse(json);
        return ParseElement(doc.RootElement);
    }

    private static DomainExpression ParseElement(JsonElement e) {
        if (e.ValueKind != JsonValueKind.Object)
            throw new ArgumentException(
                $"Expression must be a JSON object, got {e.ValueKind}.");

        // Detect which branch is present
        bool hasProp = e.TryGetProperty("property", out var propEl);
        bool hasAnd = e.TryGetProperty("and", out var andEl);
        bool hasOr = e.TryGetProperty("or", out var orEl);
        bool hasNot = e.TryGetProperty("not", out var notEl);
        bool hasLit = e.TryGetProperty("literal", out var litEl);
        bool hasRel = e.TryGetProperty("relationship", out var relEl);

        int branches = (hasProp ? 1 : 0) + (hasAnd ? 1 : 0) + (hasOr ? 1 : 0) +
                       (hasNot ? 1 : 0) + (hasLit ? 1 : 0) + (hasRel ? 1 : 0);

        if (branches == 0)
            throw new ArgumentException(
                "Expression must specify 'property', 'and', 'or', 'not', 'literal', or 'relationship'.");

        if (branches > 1)
            throw new ArgumentException(
                "Expression must specify exactly one branch (e.g. cannot set both 'property' and 'and').");

        // ── Comparison ──────────────────────────────────────────
        if (hasProp) {
            var property = propEl.GetString();
            if (string.IsNullOrWhiteSpace(property))
                throw new ArgumentException("Property name must not be empty.");

            if (!e.TryGetProperty("op", out var opEl) || opEl.ValueKind != JsonValueKind.String)
                throw new ArgumentException(
                    $"Comparison operator 'op' is required for property '{property}'.");

            var op = opEl.GetString()!;
            var value = Normalize(e.GetProperty("value"));

            var prop = DomainExpression.Property(property);
            var lit = DomainExpression.Literal(value);

            return op switch {
                "==" => DomainExpression.Equal(prop, lit),
                "!=" => DomainExpression.NotEqual(prop, lit),
                ">" => DomainExpression.GreaterThan(prop, lit),
                ">=" => DomainExpression.GreaterThanOrEqual(prop, lit),
                "<" => DomainExpression.LessThan(prop, lit),
                "<=" => DomainExpression.LessThanOrEqual(prop, lit),
                _ => throw new ArgumentException(
                    $"Unknown operator '{op}'. Supported: ==, !=, >, >=, <, <=")
            };
        }

        // ── Literal ─────────────────────────────────────────────
        if (hasLit)
            return DomainExpression.Literal(Normalize(litEl));

        // ── AND ─────────────────────────────────────────────────
        if (hasAnd) {
            if (andEl.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("'and' must be a JSON array.");

            var items = andEl.EnumerateArray().Select(ParseElement).ToArray();
            if (items.Length < 2)
                throw new ArgumentException("AND requires at least 2 operands.");

            var result = items[0];
            for (int i = 1; i < items.Length; i++)
                result = DomainExpression.And(result, items[i]);
            return result;
        }

        // ── OR ──────────────────────────────────────────────────
        if (hasOr) {
            if (orEl.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("'or' must be a JSON array.");

            var items = orEl.EnumerateArray().Select(ParseElement).ToArray();
            if (items.Length < 2)
                throw new ArgumentException("OR requires at least 2 operands.");

            var result = items[0];
            for (int i = 1; i < items.Length; i++)
                result = DomainExpression.Or(result, items[i]);
            return result;
        }

        // ── NOT ─────────────────────────────────────────────────
        if (hasNot) {
            if (notEl.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("'not' must be a JSON object.");
            return DomainExpression.Not(ParseElement(notEl));
        }

        // ── Relationship navigation (path-prefix) ──────────────
        if (hasRel) {
            var relationshipName = relEl.GetString();
            if (string.IsNullOrWhiteSpace(relationshipName))
                throw new ArgumentException("Relationship name must not be empty.");

            if (!e.TryGetProperty("inner", out var innerEl))
                throw new ArgumentException(
                    "'inner' expression is required for relationship navigation.");

            var inner = ParseElement(innerEl);
            return DomainExpression.RelationshipNav(relationshipName, inner);
        }

        throw new InvalidOperationException("Unexpected: no branch matched after validation.");
    }

    /// <summary>
    /// Converts a <see cref="JsonElement"/> to a proper CLR primitive.
    /// Numbers become <c>int</c> or <c>long</c>; booleans become <c>bool</c>;
    /// strings become <c>string</c>; null stays null.
    /// </summary>
    private static object? Normalize(JsonElement je) => je.ValueKind switch {
        JsonValueKind.Number when je.TryGetInt32(out var i) => i,
        JsonValueKind.Number when je.TryGetInt64(out var l) => l,
        JsonValueKind.Number => je.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => je.GetString(),
        JsonValueKind.Null => null,
        _ => throw new ArgumentException(
            $"Unsupported JSON value kind '{je.ValueKind}' in expression. Use number, boolean, string, or null.")
    };
}