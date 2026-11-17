namespace Poly.Introspection.CommonLanguageRuntime;

/// <summary>
/// A conversion of a CLR type: either one C# applies itself (no <see cref="Method"/>), or a reflected static
/// special-name <c>op_Implicit</c>/<c>op_Explicit</c> member (<see cref="ClrSpecialNames"/>). Instances are immutable
/// and safe for concurrent reads.
/// </summary>
[DebuggerDisplay("{ToString()}")]
internal sealed class ClrTypeConversion : ITypeConversion {
    private ClrTypeConversion(
        ConversionOperatorKind kind, ITypeDefinition source, ITypeDefinition result, ITypeMethod? method) {
        Kind = kind;
        SourceTypeDefinition = source;
        ResultTypeDefinition = result;
        Method = method;
    }

    /// <summary>A conversion the platform applies itself.</summary>
    public static ClrTypeConversion Intrinsic(
        ConversionOperatorKind kind, ITypeDefinition source, ITypeDefinition result) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);
        return new(kind, source, result, method: null);
    }

    /// <summary>A conversion implemented by a reflected one-parameter special-name member.</summary>
    public static ClrTypeConversion Declared(ConversionOperatorKind kind, ClrMethod method) {
        ArgumentNullException.ThrowIfNull(method);
        var parameters = method.Parameters.ToArray();
        if (parameters.Length != 1)
            throw new ArgumentException(
                $"Conversion '{method.Name}' takes one operand; {parameters.Length} were given.", nameof(method));
        return new(kind, parameters[0].ParameterTypeDefinition, method.MemberTypeDefinition, method);
    }

    public ConversionOperatorKind Kind { get; }
    public ITypeDefinition SourceTypeDefinition { get; }
    public ITypeDefinition ResultTypeDefinition { get; }
    public ITypeMethod? Method { get; }

    public override string ToString() =>
        $"{SourceTypeDefinition.FullName} -> {ResultTypeDefinition.FullName} ({Kind}{(Method is null ? ", applied" : "")})";
}