using Poly.Interpretation;
using Poly.Introspection;

namespace Poly.DataModeling.Interpretation;

public static class DataModelInterpretationExtensions {
    public static ITypeDefinitionProvider ToTypeDefinitionProvider(this DataModel model) {
        ArgumentNullException.ThrowIfNull(model);
        var provider = new DataModelTypeDefinitionProvider();
        
        // First pass: register all type definitions with provider reference
        foreach (var t in model.Types) {
            provider.AddTypeDefinition(new DataTypeDefinition(t, provider));
        }
        
        return provider;
    }

    public static void RegisterIn(this DataModel model, InterpretationContext context) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(context);
        context.AddTypeDefinitionProvider(model.ToTypeDefinitionProvider());
    }
}
