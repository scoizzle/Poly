namespace Poly.Text.Matching.Expressions {
    public partial class Group {
        public override TryCompareDelegate Goto()
            => gotoView(Members, Optional, Next);

        public override TryCompareDelegate Compare()
            => compare(Members, Optional, Next);

        private static TryCompareDelegate gotoView(Expression[] members) {
            if (members.Length == 0)
                return Evaluation.DefaultComparisonTrue;

            var firstGoto = members.First().Goto();

            return (StringView view) => {
                var index = view.Index;

                if (!firstGoto(view))
                    return false;

                view.Index = index;
                return true;
            };
        }

        private static TryCompareDelegate gotoView(Expression[] members, bool optional) {
            if (!optional)
                return gotoView(members);

            if (members.Length == 0)
                return Evaluation.DefaultComparisonTrue;

            var firstGoto = members.First().Goto();

            return (StringView view) => {
                var index = view.Index;

                if (firstGoto(view))
                    view.Index = index;

                return true;
            };
        }

        private static TryCompareDelegate gotoView(Expression[] members, bool optional, Expression? next) {
            if (!optional)
                return gotoView(members);

            if (next is default(Expression))
                return gotoView(members, optional);

            if (members.Length == 0)
                return Evaluation.DefaultComparisonTrue;

            var firstGoto = members.First().Goto();
            var nextGoto = next.Goto();

            return (StringView view) => {
                var index = view.Index;

                if (firstGoto(view)) {
                    view.Index = index;
                    return true;
                }

                return nextGoto(view);
            };
        }

        private static TryCompareDelegate compare(Expression[] members) {
            if (members.Length == 0)
                return Evaluation.DefaultComparisonTrue;

            var firstCompare = members.First().Compare();

            return (StringView view) => firstCompare(view);
        }

        private static TryCompareDelegate compare(Expression[] members, bool optional) {
            if (!optional)
                return compare(members);

            if (members.Length == 0)
                return Evaluation.DefaultComparisonTrue;

            var firstCompare = members.First().Compare();

            return (StringView view) => {
                var index = view.Index;

                if (firstCompare(view))
                    view.Index = index;

                return true;
            };
        }

        private static TryCompareDelegate compare(Expression[] members, bool optional, Expression? next) {
            if (!optional)
                return compare(members);

            if (next is default(Expression))
                return compare(members, optional);

            var firstCompare = members.First().Compare();
            var nextCompare = next.Compare();

            return (StringView view) => {
                var index = view.Index;

                if (firstCompare(view)) {
                    view.Index = index;
                    return true;
                }

                return nextCompare(view);
            };
        }
    }
}