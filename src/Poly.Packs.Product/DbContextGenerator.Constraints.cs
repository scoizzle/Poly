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

        return chain;
    }

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

    /// <summary>Builds the SQL predicate for a verified column's CHECK constraint.</summary>
    private static string CheckSql(string columnName, ValueRange verified) {
        var column = QuoteIdentifier(columnName);
        var parts = new List<string>();
        if (verified.Min is not null) parts.Add($"{column} >= {FormatBound(verified.Min.Value)}");
        if (verified.Max is not null) parts.Add($"{column} <= {FormatBound(verified.Max.Value)}");
        return string.Join(" AND ", parts);
    }
}