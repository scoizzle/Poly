namespace Poly.Introspection.Extensions;

public static class MemberExtensions {
    /// <summary>
    /// Filters the given members to those with the specified name.
    /// </summary>
    /// <typeparam name="T">The type of the type member.</typeparam>
    /// <param name="members">The members to filter.</param>
    /// <param name="name">The name to filter by.</param>
    /// <returns>The members with the specified name.</returns>
    public static IEnumerable<T> WithName<T>(this IEnumerable<T> members, string name) where T : ITypeMember {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(name);

        return members.Where(m => m.Name == name);
    }

    /// <summary>
    /// Filters the given members to those whose parameter types best match the specified argument types.
    /// </summary>
    /// <typeparam name="T">The type of the type member.</typeparam>
    /// <param name="members">The members to filter.</param>
    /// <param name="parameterTypes">The parameter types to match against.</param>
    /// <returns>The best-matching members, or an empty set if none found.</returns
    public static IEnumerable<T> WithParameterTypes<T>(
        this IEnumerable<T> members,
        params IEnumerable<ITypeDefinition> parameterTypes) where T : ITypeMember {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(parameterTypes);

        var bestScore = int.MaxValue;
        var bestMethods = new List<T>();
        var arguments = parameterTypes.ToList();

        foreach (var member in members) {
            if (!TryCalculateParameterScore(member, arguments, out var score)) {
                continue;
            }

            if (score > bestScore) {
                continue;
            }
            else if (score < bestScore) {
                bestScore = score;
                bestMethods = [member];
            }
            else {
                bestMethods.Add(member);
            }
        }

        return bestMethods;
    }

    /// <summary>
    /// Tries to calculate a score for how well the member's parameters match the given argument types.
    /// Lower scores indicate better matches. Returns false if the member is not compatible.
    /// </summary>
    /// <typeparam name="T">The type of the type member.</typeparam>
    /// <param name="member">The member to evaluate.</param>
    /// <param name="arguments">The argument types to match against.</param>
    /// <param name="score">The calculated score if compatible.</param>
    /// <returns>True if the member is compatible; otherwise, false.</returns>
    private static bool TryCalculateParameterScore<T>(T member, List<ITypeDefinition> arguments, out int score) where T : ITypeMember {
        score = 0;
        var parameters = member.Parameters ?? [];

        foreach (var (parameter, argument) in parameters.ZipAll(arguments)) {
            switch (parameter, argument) {
                case (null, _):
                    return false;
                case (_, null) when parameter is not null && !parameter.IsOptional:
                    return false;
                case (_, null):
                    continue; // Skip optional parameters when no argument provided
                case (_, _) when parameter.ParameterTypeDefinition == argument:
                    continue;
                case (_, _) when parameter.ParameterTypeDefinition.IsAssignableFrom(argument):
                    score += 1;
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }
}