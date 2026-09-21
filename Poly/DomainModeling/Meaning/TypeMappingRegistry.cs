using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// Session type-mapping source. Core builtins are seeded at construction; libraries
/// <see cref="Register"/> full mappings (temporal Date/DateTime/Duration); vendors
/// overlay <see cref="OverrideSqlColumnType"/> (sqlite TEXT, sqlserver nvarchar, …).
/// Lookup is <see cref="Find"/> — same shape as EF's type-mapping plugins.
/// </summary>
public sealed class TypeMappingRegistry {
    private readonly Dictionary<string, HostTypeMapping> _maps = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasLibraryMaps;

    public bool HasOverrides => _hasLibraryMaps;

    public TypeMappingRegistry() {
        foreach (var mapping in CoreMappings)
            _maps[mapping.DomainName] = mapping;
    }

    private TypeMappingRegistry(TypeMappingRegistry source) {
        foreach (var pair in source._maps)
            _maps[pair.Key] = pair.Value;
        _hasLibraryMaps = source._hasLibraryMaps;
    }

    public HostTypeMapping? Find(string domainType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        return _maps.TryGetValue(domainType, out var mapping) ? mapping : null;
    }

    public string ToSqlColumnType(string domainType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        return Find(domainType)?.StoreType ?? DomainTypeMapping.ToSqlColumnType(domainType);
    }

    public string ToClrTypeName(string domainType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        return Find(domainType)?.ClrTypeName ?? DomainTypeMapping.ToClrTypeName(domainType);
    }

    public void Register(HostTypeMapping mapping) {
        ArgumentNullException.ThrowIfNull(mapping);
        ArgumentException.ThrowIfNullOrWhiteSpace(mapping.DomainName);
        _maps[mapping.DomainName] = mapping;
        _hasLibraryMaps = true;
    }

    public void OverrideSqlColumnType(string domainType, string sqlColumnType) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlColumnType);
        _hasLibraryMaps = true;
        if (_maps.TryGetValue(domainType, out var existing))
            _maps[domainType] = existing with { StoreType = sqlColumnType };
        else
            _maps[domainType] = new HostTypeMapping(
                domainType, DomainTypeMapping.ToClrTypeName(domainType), sqlColumnType);
    }

    public void OverrideClrTypeName(string domainType, string clrTypeName) {
        ArgumentException.ThrowIfNullOrWhiteSpace(domainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(clrTypeName);
        _hasLibraryMaps = true;
        if (_maps.TryGetValue(domainType, out var existing))
            _maps[domainType] = existing with { ClrTypeName = clrTypeName };
        else
            _maps[domainType] = new HostTypeMapping(
                domainType, clrTypeName, DomainTypeMapping.ToSqlColumnType(domainType));
    }

    public TypeMappingRegistry Clone() => new(this);

    private static readonly HostTypeMapping[] CoreMappings = [
        CoreMap("Text", "string", "varchar", Prim.String),
        CoreMap("String", "string", "varchar", Prim.String),
        CoreMap("Number", "long", "bigint", Prim.Int64, nonNull: true),
        CoreMap("Int", "long", "bigint", Prim.Int64, nonNull: true),
        CoreMap("Int64", "long", "bigint", Prim.Int64, nonNull: true),
        CoreMap("Int32", "int", "integer", Prim.Int32, nonNull: true),
        CoreMap("Boolean", "bool", "boolean", Prim.Boolean, nonNull: true),
        CoreMap("Bool", "bool", "boolean", Prim.Boolean, nonNull: true),
        CoreMap("Decimal", "decimal", "decimal", Prim.Decimal, nonNull: true),
        CoreMap("Float", "double", "double precision", Prim.Float64, nonNull: true),
        CoreMap("Double", "double", "double precision", Prim.Float64, nonNull: true),
        CoreMap("Guid", "Guid", "uuid", Prim.Guid, nonNull: true, "Guid", "Empty"),
        CoreMap("Uuid", "Guid", "uuid", Prim.Guid, nonNull: true, "Guid", "Empty"),
        CoreMap("Binary", "byte[]", "binary"),
    ];

    private static HostTypeMapping CoreMap(
        string domain,
        string clr,
        string store,
        Prim? primitive = null,
        bool nonNull = false,
        string? defaultType = null,
        string? defaultMember = null) =>
        new(domain, clr, store, primitive, nonNull, defaultType, defaultMember);
}