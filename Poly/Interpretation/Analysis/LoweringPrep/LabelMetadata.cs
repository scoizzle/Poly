namespace Poly.Interpretation.Analysis.LoweringPrep;

using Poly.Syntax.Analysis;

/// <summary>Labels assigned to a <c>WhileLoop</c> node.</summary>
/// <param name="ContLabel">µop index for the condition check (loop continue point).</param>
/// <param name="EndLabel">µop index for the exit after the loop.</param>
public sealed record WhileLoopLabelMetadata(int ContLabel, int EndLabel) : IAnalysisMetadata;

/// <summary>Labels assigned to a <c>DoWhileLoop</c> node.</summary>
/// <param name="ContLabel">µop index for the body entry (loop continue point).</param>
/// <param name="EndLabel">µop index for the exit after the loop.</param>
public sealed record DoWhileLoopLabelMetadata(int ContLabel, int EndLabel) : IAnalysisMetadata;

/// <summary>Labels assigned to a <c>ForLoop</c> node.</summary>
/// <param name="CondLabel">µop index for the condition check.</param>
/// <param name="EndLabel">µop index for the exit after the loop.</param>
public sealed record ForLoopLabelMetadata(int CondLabel, int EndLabel) : IAnalysisMetadata;

/// <summary>Labels assigned to an <c>IfStatement</c> node.</summary>
/// <param name="ElseLabel">µop index for the else branch (null when no else branch).</param>
/// <param name="EndLabel">µop index for the merge point after both branches.</param>
public sealed record IfLabelMetadata(int? ElseLabel, int EndLabel) : IAnalysisMetadata;

/// <summary>Labels assigned to a <c>Conditional</c> node.</summary>
/// <param name="FalseLabel">µop index for the if-false branch.</param>
/// <param name="EndLabel">µop index for the merge point after both branches.</param>
public sealed record ConditionalLabelMetadata(int FalseLabel, int EndLabel) : IAnalysisMetadata;

/// <summary>Tracks the enclosing loop scope for Break/Continue resolution.</summary>
/// <param name="ContLabel">The continue target of the enclosing loop.</param>
/// <param name="EndLabel">The exit target of the enclosing loop.</param>
internal sealed record LoopScope(int ContLabel, int EndLabel);

/// <summary>Resolved label target for a BreakStatement.</summary>
/// <param name="TargetLabel">The EndLabel of the nearest enclosing loop.</param>
public sealed record BreakTargetMetadata(int TargetLabel) : IAnalysisMetadata;

/// <summary>Resolved label target for a ContinueStatement.</summary>
/// <param name="TargetLabel">The ContLabel of the nearest enclosing loop.</param>
public sealed record ContinueTargetMetadata(int TargetLabel) : IAnalysisMetadata;

/// <summary>Signals that φ may be needed at the merge point between two labels.
/// The assembly step compares the ring states at <c>ThenExitLabel</c> and
/// <c>ElseExitLabel</c> and stamps φ on the merge consumer if they differ.</summary>
public sealed record PhiPendingMetadata(int ThenExitLabel, int ElseExitLabel) : IAnalysisMetadata;