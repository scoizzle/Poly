namespace Poly.Interpretation.Vm;

using Poly.Syntax;

public readonly record struct SourceRange(Node Node, int FirstProgramCounter, int LastProgramCounterInclusive);