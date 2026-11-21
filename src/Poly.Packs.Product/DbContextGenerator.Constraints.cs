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
        var columnType = col.ColumnType;
        if (col.IsEnum
            && (string.Equals(columnType, col.ClrTypeName, StringComparison.Ordinal)
                || string.Equals(columnType, col.DomainType, StringComparison.Ordinal)))
            columnType = "INTEGER";
        chain = new Invoke(
            new Member(chain, "HasColumnType"),
            new Constant(columnType)
        );
        if (col.IsEnum) {
            chain = new Invoke(new Member(chain, "HasConversion")) {
                TypeArguments = [TypeReference.To(typeof(int))]
            };
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
        else if ((propName.Equals("Email", StringComparison.Ordinal)
                  || propName.Equals("EmailAddress", StringComparison.Ordinal))
                 && col.Constraints.Any(c => c is PatternConstraint)) {
            chain = new Invoke(
                new Member(chain, "HasMaxLength"),
                new Constant(256)
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

    /// <summary>Builds the SQL predicate for a verified column's CHECK constraint.</summary>
    private static string CheckSql(string columnName, ValueRange verified) {
        var parts = new List<string>();
        if (verified.Min is not null) parts.Add($"{columnName} >= {FormatBound(verified.Min.Value)}");
        if (verified.Max is not null) parts.Add($"{columnName} <= {FormatBound(verified.Max.Value)}");
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