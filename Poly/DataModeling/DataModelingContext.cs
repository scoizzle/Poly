using System.Linq.Expressions;

using Poly.Interpretation;

namespace Poly.DataModeling;

public sealed class DataModelingContext {
    private readonly DataModelTypeDefinitionProvider _typeDefinitionProvider;
    private readonly InterpretationContext<Expression> _interpretationContext;

    public DataModelingContext()
    {
        _typeDefinitionProvider = new DataModelTypeDefinitionProvider();
        _interpretationContext = new InterpretationContext<Expression>(_typeDefinitionProvider, null!);
    }
}