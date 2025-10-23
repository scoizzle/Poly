namespace Poly.Text.Matching.Expressions {
    public partial class Range {
        public override TryCompareDelegate Goto() => Evaluation.DefaultComparisonFalse;

        public override TryCompareDelegate Compare() => Evaluation.DefaultComparisonFalse;
    }
}