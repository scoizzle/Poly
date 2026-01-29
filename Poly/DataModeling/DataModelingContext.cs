namespace Poly.DataModeling;

public sealed class DataModelingContext {
    private readonly DataModelTypeDefinitionProvider _typeDefinitionProvider;

    public DataModelingContext()
    {
        _typeDefinitionProvider = new DataModelTypeDefinitionProvider();
    }
}