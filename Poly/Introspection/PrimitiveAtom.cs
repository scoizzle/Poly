namespace Poly.Introspection;

/// <summary>
/// A primitive a platform states that the shared <see cref="PrimitiveType"/> vocabulary does not name: the platform's
/// own atom (a protocol host's <c>Decimal128</c>, say). Its meaning is the platform's, so an atom is compared by the
/// platform that states it, and a consumer that knows only the shared vocabulary treats it as an unnamed primitive.
/// </summary>
internal readonly record struct PrimitiveAtom(string Name);