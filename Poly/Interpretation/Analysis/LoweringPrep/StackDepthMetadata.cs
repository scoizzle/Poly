namespace Poly.Interpretation.Analysis.LoweringPrep;

using Poly.Syntax.Analysis;

/// <summary>
/// Records the number of values a node expects on the eval stack at entry
/// and leaves on it at exit.  Used by <see cref="LoweringPrepPass"/>
/// to provide stack-height information for lowering assembly.
/// </summary>
/// <param name="EntryDepth">Values consumed from the surrounding stack before this node executes.</param>
/// <param name="ExitDepth">Values left on the surrounding stack after this node executes.</param>
public sealed record StackDepthMetadata(int EntryDepth, int ExitDepth) : IAnalysisMetadata;

/// <summary>
/// Maximum variable slot depth used across all Lambdas in the program.
/// Attached to the root node by <c>UopGenerationPass</c> after processing all
/// scopes.  The assembly step reads this to set <c>LoweringResult.MaxActiveLocalsDepth</c>.
/// </summary>
public sealed record MaxLocalsDepthMetadata(int Depth) : IAnalysisMetadata;