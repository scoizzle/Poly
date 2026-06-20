namespace Poly.Interpretation.Vm;

using Poly.Interpretation.Vm.Instructions;

public sealed record LoweringResult(List<Instruction> Instructions);