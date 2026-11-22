using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Ontology;

using Poly.Interpretation.CSharp;

namespace Poly.Packs.Product;

// DbContextGenerator (Constraints) — column fluent chain (type, required, length, default) and CHECK SQL.
public sealed partial class DbContextGenerator {
    /// <summary>Builds Syntax IR for a column's fluent configuration chain.</summary>
    private Node BuildColumnConfigNode(Parameter bParam, StorageColumn col) {
        var colName = col.ColumnName;
        var propName = col.Name;
        var needsHasColumnName = !string.Equals(colName, propName, StringComparison.Ordinal);

        // b.Property(x => x.PropName)
        var xParam = new Parameter("x");
        Node chain = new Invoke(
            new Member(bParam, "Property"),
            new Lambda([xParam], new Member(xParam, propName))
        );

        // .HasColumnName("name")
        if (needsHasColumnName) {
            chain = new Invoke(
                new Member(chain, "HasColumnName"),
                new Constant(colName)
            );
        }

        // Enum columns store the int backing value. The CLR type name is not a SQL type.
        // A declared length is expressed by HasMaxLength. An explicit HasColumnType would carry the
        // provider's unbounded type (nvarchar(max)) and stay on the column, so the unique index EF builds
        // for it is invalid (SQL Server error 1919); a length-bounded Text therefore leaves the type to EF,
        // which derives nvarchar(n) from the max length (#590). An unbounded Text keeps its mapped type.
        var columnType = col.ColumnType;
        if (col.IsEnum
            && (string.Equals(columnType, col.ClrTypeName, StringComparison.Ordinal)
                || string.Equals(columnType, col.DomainType, StringComparison.Ordinal)))
            columnType = "INTEGER";
        var derivesTypeFromLength = col.MaxLength is not null && col.DomainType is "Text" or "String";
        if (!derivesTypeFromLength) {
            chain = new Invoke(
                new Member(chain, "HasColumnType"),
                new Constant(columnType)
            );
        }
        if (col.IsEnum) {
            chain = new Invoke(new Member(chain, "HasConversion")) {
                TypeArguments = [TypeReference.To(typeof(int))]
            };
        }
        else if (string.Equals(col.ClrTypeName, "DateTime", StringComparison.Ordinal)) {
            chain = new Invoke(new Member(chain, "HasConversion"),
                UtcToStore(col.IsRequired), UtcFromStore(col.IsRequired));
        }

        // .IsRequired()
        if (col.IsRequired && !DomainTypeMapping.IsNonNullableClrValueType(col.ClrTypeName)) {
            chain = new Invoke(new Member(chain, "IsRequired"));
        }

        // .HasMaxLength(n)
        if (col.MaxLength is not null) {
            chain = new Invoke(
                new Member(chain, "HasMaxLength"),
                new Constant((int)col.MaxLength.Value)
            );
        }

        // .HasDefaultValue(...) — a literal or enum-member default the database can hold, so a row inserted outside
        // the domain takes the value Create would have written (#563). A runtime default (now/today/guid) is the
        // host's to compute and stays off the schema.
        if (DefaultValueNode(col) is { } defaultValue)
            chain = new Invoke(new Member(chain, "HasDefaultValue"), defaultValue);

        return chain;
    }

    /// <summary>
    /// Writes a <c>DateTime</c> column as the instant it names and reads it back as <c>Utc</c>. The store keeps a
    /// clock time and no kind, so a value read back was <c>Unspecified</c> and the GET text lost its <c>Z</c> (#937).
    /// <c>Local</c> is written as its UTC instant; <c>Utc</c> and <c>Unspecified</c> keep their clock time — the DSL's
    /// <c>Now</c> is one UTC clock, so an absent marker means that same clock.
    /// </summary>
    private static Node UtcToStore(bool nonNullable) {
        var v = new Parameter("v");
        var value = nonNullable ? (Node)v : new Member(v, "Value");
        Node asUtc = new Conditional(
            new Equal(new Member(value, "Kind"), new Member(new TypeReference("DateTimeKind"), "Local")),
            new Invoke(new Member(value, "ToUniversalTime")),
            new Invoke(new Member(new TypeReference("DateTime"), "SpecifyKind"),
                value, Utc()));
        return new Lambda([v], nonNullable ? asUtc : WhenPresent(v, asUtc));
    }

    /// <summary>The other half of <see cref="UtcToStore"/>: the value a stored <c>DateTime</c> column reads as.</summary>
    private static Node UtcFromStore(bool nonNullable) {
        var v = new Parameter("v");
        Node asUtc = new Invoke(new Member(new TypeReference("DateTime"), "SpecifyKind"),
            nonNullable ? (Node)v : new Member(v, "Value"), Utc());
        return new Lambda([v], nonNullable ? asUtc : WhenPresent(v, asUtc));
    }

    /// <summary><c>v == null ? (DateTime?)null : <paramref name="whenPresent"/></c> — an optional column's converter.
    /// The cast is what types the converter's provider side as <c>DateTime?</c>; an untyped <c>null</c> arm leaves the
    /// conversion inferable only as an identity, which finds no <c>HasConversion</c> overload that takes two lambdas.</summary>
    private static Node WhenPresent(Node v, Node whenPresent) =>
        new Conditional(new Equal(v, new Constant(null!)),
            new TypeCast(new Constant(null!), new OptionalTypeReference(new TypeReference("DateTime"))),
            whenPresent);

    private static Node Utc() => new Member(new TypeReference("DateTimeKind"), "Utc");

    /// <summary>
    /// The C# node a column's <see cref="DefaultValueConstraint"/> states, or null when the database cannot hold it.
    /// Mirrors <c>DomainToCSharpExporter.LowerDefaultConstantNode</c> so the column's default and Create's agree: a
    /// literal (bool, string, integral — decimal when the column maps to <c>decimal</c>) or an enum member; a runtime
    /// default (<c>now</c>, <c>today</c>, <c>guid</c>) is computed by the host and gets no column default.
    /// </summary>
    private Node? DefaultValueNode(StorageColumn col) {
        if (col.Constraints.OfType<DefaultValueConstraint>().FirstOrDefault()?.Expression is not { } expression)
            return null;

        if (expression is Literal { Value: var literal }) {
            return literal switch {
                bool or string => new Constant(literal),
                // The column's own CLR type decides the constant, as TargetTypeAdapter.IsDecimal does in lowering.
                long integral => new Constant(ColumnConstant(col, integral)),
                int narrow => new Constant(ColumnConstant(col, narrow)),
                _ => null,
            };
        }

        if (expression is PropertyAccess access && col.IsEnum
            && _domain.Types.OfType<EnumType>().Any(e =>
                string.Equals(e.Name, col.DomainType, StringComparison.Ordinal)
                && e.MemberNames.Contains(access.Name, StringComparer.Ordinal)))
            return new Member(new NamedTypeReference(col.DomainType), access.Name);

        return null;
    }

    /// <summary>An integral default in the column's CLR type: <c>decimal</c> for a decimal column, <c>int</c> for an
    /// int column, otherwise the <c>long</c> the DSL parses. Each arm converts to <c>object</c> explicitly: arms
    /// unify on the best common type otherwise, which would box every arm as <c>decimal</c>.</summary>
    private static object ColumnConstant(StorageColumn col, long value) => col.ClrTypeName switch {
        "decimal" => (decimal)value,
        "int" => (object)(int)value,
        _ => value,
    };

    /// <summary>Builds Syntax IR for a collection navigation's property access mode config.</summary>
    /// <summary>Formats a numeric bound for a SQL CHECK constraint. The decimal keeps its declared
    /// scale (<c>range(0.00, 100.00)</c> prints <c>0.00</c>), and every digit is exact (V-05b S3/A13).</summary>
    private static string FormatBound(decimal d) =>
        d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Quotes a column identifier for a SQL expression. Double quotes are the standard
    /// form every shipped provider accepts (SQLite, SQL Server with <c>QUOTED_IDENTIFIER ON</c>,
    /// PostgreSQL), and an unquoted name that is a keyword (<c>Limit</c>, <c>Order</c>, <c>Group</c>)
    /// is a syntax error in the DDL (#632). An embedded quote is doubled, per the SQL standard.</summary>
    private static string QuoteIdentifier(string name) =>
        "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>The same bound as a REAL literal (<c>1000000.0</c>), for a CHECK whose column side is
    /// <c>CAST(… AS REAL)</c>. SQLite compares an INTEGER literal with a REAL exactly, without rounding the integer, so a
    /// whole-number bound above 2^53 (which no double holds) would refuse the value at the bound; a REAL literal is
    /// parsed by the same routine as the cast, and the two sides round together. The digits and the declared scale are
    /// unchanged.</summary>
    private static string FormatRealBound(decimal d) {
        var text = FormatBound(d);
        return text.Contains('.') ? text : text + ".0";
    }

    /// <summary>True when this numeric column is stored in a text-affinity column: SQLite maps <c>Decimal</c> to
    /// <c>TEXT</c>, the one store there that keeps every digit. Its CHECK must compare the value as a number (#559).
    /// This is SQLite's mapping today; a pack that maps a range-checked <c>Decimal</c> to text on a dialect whose
    /// <c>REAL</c> is not an IEEE double (SQL Server's is single precision) must extend this CHECK for its dialect, not
    /// inherit it.</summary>
    private static bool StoresNumberAsText(StorageColumn column) =>
        string.Equals(column.DomainType, "Decimal", StringComparison.Ordinal)
        && string.Equals(column.ColumnType, "TEXT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the SQL predicate for a verified column's CHECK constraint.</summary>
    /// <remarks>
    /// A TEXT-affinity value compares to a numeric literal as text, so <c>declared &lt;= 1000000</c> was false for every
    /// value above <c>'1'</c> and SQLite refused valid decimals at <c>SaveChanges</c> (#559). The stored decimal is still a
    /// number, so its CHECK compares it as one: <c>CAST(column AS REAL)</c>, and the bound is a REAL literal for the same
    /// reason. Both sides are then the same correctly-rounded double, and rounding is monotone, so a value inside the
    /// declared range is never refused. The CHECK is a floor that never contradicts <c>Create</c>, never a second stricter
    /// guard: a value that differs from a bound beyond REAL's 15–17 significant digits may pass it, and <c>Create</c> still
    /// refuses that value in the domain. It is not a type guard either: text that is not a number casts to <c>0.0</c>, so
    /// it passes a range that contains 0 (a write outside the module, where <c>Create</c> does not run). Every numeric store
    /// type keeps its plain comparison.
    /// </remarks>
    private static string CheckSql(StorageColumn column) {
        var verified = column.VerifiedRange!;
        var text = StoresNumberAsText(column);
        var name = QuoteIdentifier(column.ColumnName);
        var value = text ? $"CAST({name} AS REAL)" : name;
        string Bound(decimal bound) => text ? FormatRealBound(bound) : FormatBound(bound);
        var parts = new List<string>();
        if (verified.Min is not null) parts.Add($"{value} >= {Bound(verified.Min.Value)}");
        if (verified.Max is not null) parts.Add($"{value} <= {Bound(verified.Max.Value)}");
        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// The SQL predicate a text column's declared <see cref="LengthConstraint"/> states, or null when it states
    /// nothing. Built from the constraint, not <see cref="StorageColumn.MaxLength"/> (Email's 256 is an unrelated
    /// mapping fallback). Never stricter than Create: SQLite's <c>length()</c> counts code points and
    /// <c>length(CAST(… AS BLOB))</c> counts UTF-8 bytes, while .NET's string Length counts UTF-16 units — code points
    /// ≤ units ≤ bytes — so a value Create accepts is never refused. Read-side only: Create and assign guard writes.
    /// </summary>
    private static string? LengthCheckSql(StorageColumn col) {
        if (!string.Equals(col.ClrTypeName, "string", StringComparison.Ordinal))
            return null;
        if (col.Constraints.OfType<LengthConstraint>().FirstOrDefault() is not { } length)
            return null;

        var parts = new List<string>();
        if (length.MinLength > 0)
            parts.Add($"length(CAST({col.ColumnName} AS BLOB)) >= {length.MinLength}");
        if (length.MaxLength < int.MaxValue)
            parts.Add($"length({col.ColumnName}) <= {length.MaxLength}");
        return parts.Count == 0 ? null : string.Join(" AND ", parts);
    }
}