namespace Poly.DomainModeling.Ontology;

/// <summary>The built-in numeric functions a call may name (#547). One kind per name, no overload by arity.</summary>
public enum BuiltinNumericFunction {
    Round,
    Abs,
    Min,
    Max,
    Floor,
    Ceiling
}

/// <summary>
/// One built-in numeric call: the name it is written with, the kind it means, and the parameter names its
/// diagnostics use. A domain that declares a function with the same name wins (see
/// <see cref="BuiltinFunctions.Find(Domain?, string)"/>), so this table never shadows author code.
/// </summary>
public sealed record BuiltinFunction(string Name, BuiltinNumericFunction Function, IReadOnlyList<string> Parameters);

/// <summary>
/// The six built-in numeric functions, all on <c>Number</c> and <c>Decimal</c>: <c>Round</c> (the money rule,
/// midpoint away from zero), <c>Abs</c>, <c>Min</c>, <c>Max</c>, <c>Floor</c> and <c>Ceiling</c>. The table
/// carries names and arity only; analysis states each call's type and lowering emits its one
/// <c>System.Math</c> call, so the interpreter and the printed C# execute the same meaning.
/// </summary>
public static class BuiltinFunctions {
    public static IReadOnlyList<BuiltinFunction> All { get; } = [
        new("Round", BuiltinNumericFunction.Round, ["value", "digits"]),
        new("Abs", BuiltinNumericFunction.Abs, ["value"]),
        new("Min", BuiltinNumericFunction.Min, ["left", "right"]),
        new("Max", BuiltinNumericFunction.Max, ["left", "right"]),
        new("Floor", BuiltinNumericFunction.Floor, ["value"]),
        new("Ceiling", BuiltinNumericFunction.Ceiling, ["value"]),
    ];

    private static readonly Dictionary<string, BuiltinFunction> ByName =
        All.ToDictionary(f => f.Name, StringComparer.Ordinal);

    /// <summary>The built-in a call names, or null when no built-in has that name.</summary>
    public static BuiltinFunction? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>
    /// The built-in a call names, or null when no built-in has that name or the domain declares a function
    /// with it: a declared function wins, as it does over a contract endpoint (#426).
    /// </summary>
    public static BuiltinFunction? Find(Domain? domain, string name) =>
        domain is not null && domain.Functions.Any(f => string.Equals(f.Name, name, StringComparison.Ordinal))
            ? null
            : Find(name);
}