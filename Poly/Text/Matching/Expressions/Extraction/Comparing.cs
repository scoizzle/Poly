using System;

namespace Poly.Text.Matching.Expressions {
    public partial class Extraction {
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