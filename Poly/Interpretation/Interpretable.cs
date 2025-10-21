namespace Poly.Interpretation;

public abstract class Interpretable {
    public abstract Expression BuildExpression(Context context);
}