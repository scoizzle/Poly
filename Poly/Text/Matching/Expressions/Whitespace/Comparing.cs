namespace Poly.Text.Matching.Expressions {
    public partial class Whitespace {
        public override TryCompareDelegate Goto() =>
            gotoView(Optional, Next);

        public override TryCompareDelegate Compare() =>
            compare(Optional, Next);

        private static TryCompareDelegate gotoView() =>
            (StringView view) => view.GotoAndConsume(char.IsWhiteSpace);

        private static TryCompareDelegate gotoView(bool optional) {
            if (!optional)
                return gotoView();
                
            return (StringView view) => {
                view.ConsumeUntil(char.IsWhiteSpace);
                return true;
            };
        }

        private static TryCompareDelegate gotoView(Expression? next) {
            if (next is default(Expression))
                return gotoView();

            var nextCompare = next.Compare();

            return (StringView view) => {
                var index = view.Index;

                if (!view.GotoAndConsume(char.IsWhiteSpace))
                    return false;
                
                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                return true;
            };
        }

        private static TryCompareDelegate gotoView(bool optional, Expression? next) {
            if (!optional)
                return gotoView(next);

            if (next is default(Expression))
                return gotoView(optional);

            var nextCompare = next.Compare();

            return (StringView view) => {
                var index = view.Index;
                view.GotoAndConsume(char.IsWhiteSpace);
                
                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                return true;
            };
        }

        private static TryCompareDelegate compare() =>
            (StringView view) => view.Consume(char.IsWhiteSpace);

        private static TryCompareDelegate compare(bool optional)  {
            if (!optional)
                return compare();
                
            return (StringView view) => {
                view.Consume(char.IsWhiteSpace);
                return true;
            };
        }

        private static TryCompareDelegate compare(Expression? next) {
            if (next is default(Expression))
                return compare();

            var nextCompare = next.Compare();

            return (StringView view) => {
                var index = view.Index;

                if (!view.Consume(char.IsWhiteSpace))
                    return false;
                
                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                return true;
            };
        }

        private static TryCompareDelegate compare(bool optional, Expression? next) {
            if (!optional)
                return compare(next);

            if (next is default(Expression))
                return compare(optional);

            var nextCompare = next.Compare();

            return (StringView view) => {
                var index = view.Index;
                view.Consume(char.IsWhiteSpace);
                
                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                return true;
            };
        }
    }
}