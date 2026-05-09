using System.Reflection;
using System.Text.Json;

namespace Poly.Data.Modeling.Recipes.Contracts;

/// <summary>
/// Discriminated source descriptor for contract import recipes.
/// </summary>
public abstract record ContractImportSource {
    public sealed record OpenApiDocument(JsonDocument Document, string Version) : ContractImportSource;
    public sealed record OpenApiJson(string Json, string Version) : ContractImportSource;
    public sealed record ClrType(Type RootType, string Version) : ContractImportSource;
    public sealed record ClrAssembly(Assembly Assembly, string Version, Func<Type, bool>? TypeFilter = null) : ContractImportSource;
}