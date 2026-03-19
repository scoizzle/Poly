namespace Poly.Text.Matching.Expressions {
    public partial class Static : Expression {
        public Static(string value, bool optional = false, int minimumLength = 0) : base(optional, minimumLength) {
            Value = value;
        }

        public string Value { get; }

        public static bool Parse(StringView view, out Expression? expression) {
            //if (view.Extract(SelectStatic, out var content)) {
            //    var value = Conversion.Descape(content);
            //    var minimumLength = value.Length;
            //
            //    expression = new Static(value, optional: false, minimumLength);
            //    return true;
            //}

            expression = default;
            return false;
        }

        private static bool SelectStatic(StringView view) {
            while (!view.IsEmpty) {
                if (view.ConsumeUntil(IsToken)) {
                    if (view.First == '\\') {
                        view.Consume();
                    }
                    else {
                        break;
                    }
                }
                else {
                    view.Consume(view);
                    break;
                }
            }

            return true;
        }

        private static bool IsToken(char c) {
            return c switch {
                '*' or '^' or '{' or '(' or '?' or '[' => true,
                _ => false,
            };
        }
    }
}