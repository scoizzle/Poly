namespace Poly.Introspection;

/// <summary>
/// The operator chosen for a pair of operand types. The operand conversions it needs are
/// <c>parameter.ResolveConversionFrom(operand)</c> for each parameter of <see cref="Signature"/>.
/// </summary>
internal readonly record struct OperatorResolution(ITypeMethod Signature, bool PlatformApplied) {
    public ITypeDefinition Result => Signature.MemberTypeDefinition;
}