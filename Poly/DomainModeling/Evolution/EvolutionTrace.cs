using System;
using System.Collections.Generic;

using Poly.DomainModeling.Dispatch;
using Poly.DomainModeling.Ontology;
using Poly.DomainModeling.Ontology.Contract;
using Poly.DomainModeling.Runtime;

using Action = Poly.DomainModeling.Ontology.Action;
using Add = Poly.DomainModeling.Ontology.Add;
using And = Poly.DomainModeling.Ontology.And;
using Divide = Poly.DomainModeling.Ontology.Divide;
using Multiply = Poly.DomainModeling.Ontology.Multiply;
using Not = Poly.DomainModeling.Ontology.Not;
using Or = Poly.DomainModeling.Ontology.Or;
using PrimitiveType = Poly.DomainModeling.Ontology.PrimitiveType;
using Subtract = Poly.DomainModeling.Ontology.Subtract;
using ValueType = Poly.DomainModeling.Ontology.ValueType;

namespace Poly.DomainModeling.Evolution;

/// <summary>
/// Captures what happened during an evolution operation (successful or rejected).
/// Designed to be rich enough for LLM/MCP agents and future real-time UIs while remaining simple.
/// Steps carry the ordered natural-language descriptions (also emitted as Information diagnostics).
/// The RolledBack flag and diagnostics in the accompanying AnalysisResult tell the caller
/// whether the proposed changes were rejected (no actual rollback occurs — the model is immutable).
/// </summary>
public sealed record EvolutionTrace(
    IReadOnlyList<EvolutionStep> Steps,
    bool RolledBack,
    TimeSpan Duration,
    int ErrorCount,
    int WarningCount
);

public sealed record EvolutionStep(
    string ChangeDescription
);