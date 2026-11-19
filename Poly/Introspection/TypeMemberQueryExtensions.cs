namespace Poly.Introspection;

public static class TypeMemberQueryExtensions {
    extension<T>(IEnumerable<T> members) where T : ITypeMember {
        /// <summary>
        /// Filters the given members to those with the specified name.
        /// </summary>
        /// <typeparam name="T">The type of the type member.</typeparam>
        /// <param name="members">The members to filter.</param>
        /// <param name="name">The name to filter by.</param>
        /// <returns>The members with the specified name.</returns>
        public IEnumerable<T> WithName(string name) {
            ArgumentNullException.ThrowIfNull(members);
            ArgumentNullException.ThrowIfNull(name);

            return members.Where(m => m.Name == name);
        }

        /// <summary>
        /// Filters the given members to those whose parameter types best match the specified argument types, ranked by
        /// <paramref name="system"/>'s assignability and conversions (#998 T4b, T9).
        /// </summary>
        public IEnumerable<T> WithParameterTypes(ITypeSystem system, IEnumerable<ITypeDefinition> parameterTypes) {
            ArgumentNullException.ThrowIfNull(members);
            ArgumentNullException.ThrowIfNull(parameterTypes);

            var bestScore = int.MaxValue;
            var bestMethods = new List<T>();
            var arguments = parameterTypes.ToList();

            foreach (var member in members) {
                switch (CalculateParameterScore(member, arguments, system)) {
                    case null:
                        continue;
                    case int score when score > bestScore:
                        continue;
                    case int score when score < bestScore:
                        bestScore = score;
                        bestMethods = [member];
                        break;
                    case int score when score == bestScore:
                        bestMethods.Add(member);
                        break;
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
        private static int? CalculateParameterScore(T member, List<ITypeDefinition> arguments, ITypeSystem system) {
            var score = 0;
            var parameters = member.Parameters ?? [];

            foreach (var (parameter, argument) in parameters.ZipAll(arguments)) {
                switch (parameter, argument) {
                    case (null, _):
                        return null; // Too many arguments
                    case (_, null) when parameter is not null && !parameter.IsOptional:
                        return null; // Missing required argument
                    case (_, null):
                        continue; // Skip optional parameters when no argument provided
                    case (_, _) when TypeDefinitionExtensions.IsSameType(parameter.ParameterTypeDefinition, argument):
                        continue;
                    case (_, _) when parameter.ParameterTypeDefinition.IsAssignableFrom(argument, system):
                        score++; // Assignable but not exact match
                        continue;
                    case (_, _) when TypeDefinitionExtensions.ConversionInto(parameter.ParameterTypeDefinition, argument, system) is { Kind: ConversionOperatorKind.Implicit }:
                        score += 2; // A conversion the platform applies itself ranks below assignable (a declared one is assignable)
                        continue;
                    default:
                        return null; // Incompatible types
                }
            }

            return score;
        }
    }
}