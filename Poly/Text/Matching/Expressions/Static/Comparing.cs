namespace Poly.Text.Matching.Expressions
{
    public partial class Static {
        public override TryCompareDelegate Goto() =>
            gotoView(Value, Optional, Next);

        public override TryCompareDelegate Compare() =>
            compare(Value, Optional, Next);

        private static TryCompareDelegate gotoView(string value) =>
            (StringView view) => view.Goto(value);

        private static TryCompareDelegate gotoView(string value, bool optional) {
            if (!optional)
                return gotoView(value);
                
            return (StringView view) => {
                view.GotoAndConsume(char.IsWhiteSpace);
                return true;
            };
        }

        private static TryCompareDelegate gotoView(string value, Expression next) {
            if (next is default(Expression))
                return gotoView(value);

            var valueLength = value.Length;
            var nextCompare = next.Compare();
                
            return (StringView view) => {
                var index = view.Index;

                if (!view.Goto(value))
                    return false;

                var offset = view.Index;
                view.Consume(valueLength);

                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                view.Index = offset;
                return true;
            };
        }

        private static TryCompareDelegate gotoView(string value, bool optional, Expression next) {
            if (!optional)
                return gotoView(value, next);

            if (next is default(Expression))
                return gotoView(value, optional);

            var valueLength = value.Length;
            var nextCompare = next.Compare();
                
            return (StringView view) => {
                var index = view.Index;
                var offset = index;

                if (view.Goto(value)) {
                    offset = view.Index;
                    view.Consume(valueLength);
                }

                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                view.Index = offset;
                return true;
            };
        }

        private static TryCompareDelegate compare(string value) =>
            (StringView view) => view.Consume(value);

        private static TryCompareDelegate compare(string value, bool optional) {
            if (!optional)
                return compare(value);
                
            return (StringView view) => {
                view.Consume(value);
                return true;
            };
        }

        private static TryCompareDelegate compare(string value, Expression next) {
            if (next is default(Expression))
                return compare(value);

            var nextCompare = next.Compare();
                
            return (StringView view) => {
                var index = view.Index;

                if (!view.Consume(value))
                    return false;

                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                return true;
            };
        }

        private static TryCompareDelegate compare(string value, bool optional, Expression next) {
            if (!optional)
                return compare(value, next);

            if (next is default(Expression))
                return compare(value, optional);

            var nextCompare = next.Compare();
                
            return (StringView view) => {
                var index = view.Index;
                view.Consume(value);

                if (!nextCompare(view)) {
                    view.Index = index;
                    return false;
                }

                return true;
            };
        }
    }
}