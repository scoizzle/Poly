using Poly.Ast;
using Poly.Ast.Nodes;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Runtime;
using Poly.Interpretation.CSharp;
using Poly.Introspection;

using ClrPrimitive = Poly.Introspection.PrimitiveType;

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
                TypeArguments = [new PrimitiveTypeReference(ClrPrimitive.Int32)]
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
    /// <summary>Formats a numeric bound for a SQL CHECK constraint.</summary>
    private static string FormatBound(double d) =>
        d == Math.Floor(d) ? d.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("G", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Builds the SQL predicate for a verified column's CHECK constraint.</summary>
    private static string CheckSql(string columnName, ValueRange verified) {
        var parts = new List<string>();
        if (verified.Min is not null) parts.Add($"{columnName} >= {FormatBound(verified.Min.Value)}");
        if (verified.Max is not null) parts.Add($"{columnName} <= {FormatBound(verified.Max.Value)}");
        return string.Join(" AND ", parts);
    }
}