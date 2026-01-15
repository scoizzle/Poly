namespace Poly.Text.Matching {
    public abstract class Expression {
        public Expression(bool optional, int minimumLength)
        {
            Optional = optional;
            MinimumLength = minimumLength;
        }

        public bool Optional { get; }

        public int MinimumLength { get; }

        public Expression? Previous { get; protected set; }

        public Expression? Next { get; protected set; }

        public abstract TryCompareDelegate Goto();

        public abstract TryCompareDelegate Compare();

        public virtual void Link(Expression? previous, Expression? next)
        {
            Previous = previous;
            Next = next;
        }
    }
}