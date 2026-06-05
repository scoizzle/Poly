namespace Poly.Introspection;

public static class TypeMemberExtensions {
    extension(ITypeMember typeMember) {
        /// <summary>
        /// Gets whether this is a static member.
        /// </summary>
        public bool IsStatic => typeMember.LifetimeModifier == LifetimeModifier.Static;

        /// <summary>
        /// Gets whether this member has a read capability (derived from presence of Read delegate).
        /// </summary>
        public bool CanRead => (typeMember as ITypeField)?.Read is not null
                            || (typeMember as ITypeProperty)?.Read is not null;

        /// <summary>
        /// Gets whether this member has a write capability (derived from presence of Write delegate).
        /// </summary>
        public bool CanWrite => (typeMember as ITypeField)?.Write is not null
                             || (typeMember as ITypeProperty)?.Write is not null;

        /// <summary>
        /// Gets whether this member has an initialize capability (derived from presence of Initialize delegate).
        /// </summary>
        public bool CanInitialize => (typeMember as ITypeField)?.Initialize is not null
                                  || (typeMember as ITypeProperty)?.Initialize is not null;
    }
}