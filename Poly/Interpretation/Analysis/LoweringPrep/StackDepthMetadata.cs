namespace Poly.Interpretation.Analysis.LoweringPrep;

using Poly.Syntax.Analysis;

/// <summary>
/// Records the number of values a node expects on the eval stack at entry
/// and leaves on it at exit.  Used by <see cref="StackDepthAnalysisPass"/>
/// to provide stack-height information for lowering assembly.
/// </summary>
/// <param name="EntryDepth">Values consumed from the surrounding stack before this node executes.</param>
/// <param name="ExitDepth">Values left on the surrounding stack after this node executes.</param>
public sealed record StackDepthMetadata(int EntryDepth, int ExitDepth) : IAnalysisMetadata;