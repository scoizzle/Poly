
using Poly.Interpretation;

namespace Poly.DataModeling;

public sealed class DataModelingContext {
    private readonly DataModelTypeDefinitionProvider _typeDefinitionProvider;
    private readonly InterpretationContext _interpretationContext;

    public DataModelingContext() {
        _typeDefinitionProvider = new DataModelTypeDefinitionProvider();
        _interpretationContext = new InterpretationContext();
        _interpretationContext.AddTypeDefinitionProvider(_typeDefinitionProvider);
    }
}
