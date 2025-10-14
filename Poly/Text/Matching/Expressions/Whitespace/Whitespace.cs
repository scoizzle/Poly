namespace Poly.Text.Matching.Expressions
{
    public partial class Whitespace : Expression {
        public Whitespace(bool optional = false, int minimumLength = 0) : base(optional, minimumLength)
        { }

        public static bool Parse(StringView view, out Expression? expression) {
            if (view.Consume('^')) {
                var optional = view.Consume('?');
                var minimumLength = optional ? 0 : 1;

                expression = new Whitespace(optional, minimumLength);
                return true;
            }

            expression = default;
            return false;
        }
    }
}