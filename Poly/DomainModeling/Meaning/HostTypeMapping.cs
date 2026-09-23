using Prim = Poly.Introspection.PrimitiveType;

namespace Poly.DomainModeling.Meaning;

/// <summary>
/// One domain→host type mapping (EF-style). Libraries <see cref="TypeMappingRegistry.Register"/>
/// these; vendors overlay <see cref="StoreType"/>. Core does not switch on Date/DateTime names.
/// </summary>
public sealed record HostTypeMapping(
    string DomainName,
    string ClrTypeName,
    string StoreType,
    Prim? Primitive = null,
    bool IsNonNullableValueType = false,
    string? DefaultMemberType = null,
    string? DefaultMemberName = null);