namespace Poly.Introspection.Extensions;

public static class EnumerableMemberExtensions {
    public static ITypeMember? WithParameters(this IEnumerable<ITypeMember> members, params IEnumerable<ITypeDefinition>? parameterTypes) {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        if (parameterTypes is null || !parameterTypes.Any()) {
            return members.FirstOrDefault(m => m.Parameters == null);
        }

        foreach (var member in members) {
            if (member.Parameters is null) {
                continue;
            }

            var memberParamTypes = member.Parameters.Select(p => p.ParameterTypeDefinition);

            if (memberParamTypes.SequenceEqual(parameterTypes)) {
                return member;
            }
        }

        return null;
    }
}