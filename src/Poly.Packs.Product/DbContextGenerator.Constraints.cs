using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;

using Poly.Interpretation.CSharp;

namespace Poly.Packs.Product;

// DbContextGenerator (Constraints) — column fluent chain (type, required, length) and range CHECK SQL.
public sealed partial class DbContextGenerator {
    /// <summary>Builds Syntax IR for a column's fluent configuration chain.</summary>
    private static Node BuildColumnConfigNode(Parameter bParam, StorageColumn col) {
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

        return chain;
    }

    /// <summary>Builds Syntax IR for a collection navigation's property access mode config.</summary>
    /// <summary>Formats a numeric bound for a SQL CHECK constraint. The decimal keeps its declared
    /// scale (<c>range(0.00, 100.00)</c> prints <c>0.00</c>), and every digit is exact (V-05b S3/A13).</summary>
    private static string FormatBound(decimal d) =>
        d.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True when this numeric column is stored in a text-affinity column: SQLite maps <c>Decimal</c> to
    /// <c>TEXT</c>, the one store there that keeps every digit. Its CHECK must compare the value as a number (#559).</summary>
    private static bool StoresNumberAsText(StorageColumn column) =>
        string.Equals(column.DomainType, "Decimal", StringComparison.Ordinal)
        && string.Equals(column.ColumnType, "TEXT", StringComparison.OrdinalIgnoreCase);

    /// <summary>Builds the SQL predicate for a verified column's CHECK constraint.</summary>
    /// <remarks>
    /// A TEXT-affinity value compares to a numeric literal as text, so <c>declared &lt;= 1000000</c> was false for every
    /// value above <c>'1'</c> and SQLite refused valid decimals at <c>SaveChanges</c> (#559). The stored decimal is still a
    /// number, so its CHECK compares it as one: <c>CAST(column AS REAL)</c>. Both sides are then the same correctly-rounded
    /// double, and rounding is monotone, so a value inside the declared range is never refused. The CHECK is a floor that
    /// never contradicts <c>Create</c>, never a second stricter guard: a value that differs from a bound beyond REAL's
    /// 15–17 significant digits may pass it, and <c>Create</c> still refuses that value in the domain. Every numeric store
    /// type keeps its plain comparison.
    /// </remarks>
    private static string CheckSql(StorageColumn column) {
        var verified = column.VerifiedRange!;
        var value = StoresNumberAsText(column) ? $"CAST({column.ColumnName} AS REAL)" : column.ColumnName;
        var parts = new List<string>();
        if (verified.Min is not null) parts.Add($"{value} >= {FormatBound(verified.Min.Value)}");
        if (verified.Max is not null) parts.Add($"{value} <= {FormatBound(verified.Max.Value)}");
        return string.Join(" AND ", parts);
    }
}