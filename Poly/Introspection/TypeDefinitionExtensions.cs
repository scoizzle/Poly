namespace Poly.Introspection;

public static class TypeDefinitionExtensions {
    /// <summary>
    /// Gets the best-matching method overloads for the given name and argument types.
    /// </summary>
    /// <param name="name">The method name.</param>
    /// <param name="argumentTypes">The types of the arguments to match against.</param>
    /// <returns>The best-matching methods, or an empty list if none found.</returns>
    public static List<ITypeMethod> FindMatchingMethodOverloads(
        this ITypeDefinition typeDefinition,
        string name,
        IEnumerable<ITypeDefinition> argumentTypes
    ) {
        var bestScore = int.MaxValue;
        var bestMethods = new List<ITypeMethod>();
        
        var arguments = argumentTypes.ToList();
        var overloads = typeDefinition.Methods.Where(m => m.Name == name);

        foreach (var overload in overloads) {
            var parameters = overload.Parameters.ToList();
            if (parameters.Count != arguments.Count)
                continue;

            var score = 0;
            var match = true;
            for (var i = 0; i < arguments.Count; i++) {
                var param = parameters[i];
                var arg = arguments[i];
                if (param.ParameterTypeDefinition == arg) {
                    // Exact match, score unchanged
                }
                else if (param.ParameterTypeDefinition.IsAssignableFrom(arg)) {
                    score += 1; // Penalty for conversion
                }
                else {
                    match = false;
                    break;
                }
            }

            if (!match || score > bestScore)
                continue;

            if (score < bestScore) {
                bestScore = score;
                bestMethods.Clear();
            }
            
            bestMethods.Add(overload);
        }

        return bestMethods;
    }
}