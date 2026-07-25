using Poly.DomainModeling;
using Poly.DomainModeling.Analysis;
using Poly.DomainModeling.Lowering;

namespace Poly.Packs.SqlServer;

/// <summary>
/// Storage convention that validates SQL Server identifier length limits.
///
/// SQL Server limits regular identifiers to 128 characters.
/// This convention rejects column names and table names that exceed that
/// limit with a clear <see cref="InvalidOperationException"/> (fail-closed).
/// </summary>
public sealed class SqlServerIdentifierConvention : IStorageConvention {
    /// <summary>SQL Server maximum identifier length (regular identifiers).</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>Rejects table names exceeding 128 characters.</summary>
    public StorageEntity? ProjectEntity(Entity entity, StorageEntity baseline) {
        if (baseline.TableName.Length > MaxIdentifierLength) {
            throw new InvalidOperationException(
                $"Entity '{baseline.Name}': table name '{baseline.TableName}' " +
                $"({baseline.TableName.Length} chars) exceeds SQL Server maximum " +
                $"identifier length of {MaxIdentifierLength}.");
        }
        return null;
    }

    /// <summary>Rejects column names exceeding 128 characters.</summary>
    public StorageColumn? ProjectColumn(Property property, StorageColumn baseline) {
        if (baseline.ColumnName.Length > MaxIdentifierLength) {
            throw new InvalidOperationException(
                $"Property '{property.Name}': column name '{baseline.ColumnName}' " +
                $"({baseline.ColumnName.Length} chars) exceeds SQL Server maximum " +
                $"identifier length of {MaxIdentifierLength}.");
        }
        return null;
    }
}