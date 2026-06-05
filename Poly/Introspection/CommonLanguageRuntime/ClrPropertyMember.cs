using Poly.Introspection;

namespace Poly.Introspection.CommonLanguageRuntime;

internal abstract class ClrPropertyMember : ClrTypeMember, ITypeProperty {
    public abstract MemberReadDelegate? Read { get; }
    public abstract MemberWriteDelegate? Write { get; }
    public abstract MemberWriteDelegate? Initialize { get; }
}