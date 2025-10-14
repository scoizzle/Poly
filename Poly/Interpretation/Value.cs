namespace Poly.Interpretation;

public abstract class Value {
    public abstract Expression BuildExpression(Context context);
}