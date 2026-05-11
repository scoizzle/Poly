namespace Poly.Introspection;

public static class TypeMemberExtensions {
    extension(ITypeMember typeMember) {
        /// <summary>
        /// Gets whether this is a static member.
        /// </summary>
        public bool IsStatic => typeMember.LifetimeModifier == LifetimeModifier.Static;
    }
}