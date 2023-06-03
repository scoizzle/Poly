namespace Poly.Text.Matching.Expressions
{
    public partial class Wildcard {
        public override TryCompareDelegate Goto() =>
            Next == null ?
                Evaluation.DefaultComparisonTrue :
                Next.Goto();

        public override TryCompareDelegate Compare() =>
            Next == null ?
                Evaluation.DefaultComparisonTrue :
                Next.Goto();
    }
}