namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Pack-overridable domain→host type maps for storage conventions and codegen.
///
/// Core defaults live in <see cref="DomainTypeMapping"/> (D3 generic SQL + CLR).
/// Packs register per-key overrides via <see cref="OverrideSqlColumnType"/> /
/// <see cref="OverrideClrTypeName"/>. Lookup returns the override when present,
/// otherwise the core default (D5 last-registered wins via dictionary set).
/// </summary>
public sealed class TypeMappingRegistry {
    private readonly Dictionary<string, string> _sqlOverrides = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _clrOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Looks up the SQL column type for a domain type (override or core default).</summary>
    public string ToSqlColumnType(string domainType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        return _sqlOverrides.TryGetValue(domainType, out var sqlType)
            ? sqlType
            : DomainTypeMapping.ToSqlColumnType(domainType);
    }

    /// <summary>Looks up the CLR type name for a domain type (override or core default).</summary>
    public string ToClrTypeName(string domainType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        return _clrOverrides.TryGetValue(domainType, out var clrType)
            ? clrType
            : DomainTypeMapping.ToClrTypeName(domainType);
    }

    /// <summary>Registers a per-key override for SQL column types. Last call wins.</summary>
    public void OverrideSqlColumnType(string domainType, string sqlColumnType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlColumnType);
        _sqlOverrides[domainType] = sqlColumnType;
    }

    /// <summary>Registers a per-key override for CLR type names. Last call wins.</summary>
    public void OverrideClrTypeName(string domainType, string clrTypeName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(clrTypeName);
        _clrOverrides[domainType] = clrTypeName;
    }
}