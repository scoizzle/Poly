using Poly.Introspection;

namespace Poly.DataModeling;

public static class DataModelExtensions {
    public static DataModelTypeDefinitionProvider ToTypeDefinitionProvider(this DataModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var provider = new DataModelTypeDefinitionProvider();

        foreach (var type in model.Types) {
            provider.AddDataType(type);
        }

        return provider;
    }
}
