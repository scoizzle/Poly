
namespace Poly.Interpretation;

public sealed class Parameter(string name) : Value {
    public string Name { get; } = name;

    public override Expression BuildExpression(Context context) {
        throw new NotImplementedException();
    }
}