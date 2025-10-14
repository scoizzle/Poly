namespace Poly.Text.Matching.Expressions
{
    public partial class Wildcard : Expression {          
        public Wildcard(bool optional = true, int minimumLength = 0) : base(optional, minimumLength) 
        { }

        public static bool Parse(StringView view, out Expression? expression) {
            if (view.Consume('*')) {
                expression = new Wildcard(optional: true, minimumLength: 0);
                return true;
            }

            expression = default;
            return false;
        }
    }
}