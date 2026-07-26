using Poly.Ast.Nodes;

namespace Poly.DomainModeling.Lowering;

/// <summary>
/// Optional extension point for storage-specific syntax decoration.
/// Tool packs implement this interface to inject storage concerns
/// (table mappings, provider-specific annotations, query filters)
/// into the CompilationUnitNode trees produced by DbContextGenerator
/// and MinimalApiGenerator.
///
/// When null (default), generators emit storage-agnostic output.
/// </summary>
public interface IStorageSyntaxEmitter {
    /// <summary>
    /// Decorates a DbContext CompilationUnitNode with storage-specific
    /// mappings, provider calls, and annotations.
    /// </summary>
    /// <param name="tree">The generator-produced compilation unit.</param>
    /// <param name="storage">Storage mapping metadata (logical column/key/table shapes).</param>
    /// <returns>The decorated compilation unit.</returns>
    CompilationUnitNode EmitDbContext(CompilationUnitNode tree, object storage);

    /// <summary>
    /// Decorates a Minimal API CompilationUnitNode with storage-specific
    /// queryable endpoint filters and query parameter conventions.
    /// </summary>
    /// <param name="tree">The generator-produced compilation unit.</param>
    /// <param name="storage">Storage mapping metadata.</param>
    /// <param name="queryable">Optional queryable endpoint definitions for policy-backed filtering.</param>
    /// <returns>The decorated compilation unit.</returns>
    CompilationUnitNode EmitApi(CompilationUnitNode tree, object storage,
        IReadOnlyList<object>? queryable);
}