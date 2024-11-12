namespace Poly.Text.Matching.Expressions
{
    public partial class Range : Expression
    {
        public Range(Func<char, bool> validator, bool optional = false, int minimumLength = 0) : base(optional, minimumLength)
        {
            Validator = validator;
        }

        public Func<char, bool> Validator { get; }

        public static bool Parse(StringView view, out Expression expression)
        {
            if (view.ExtractBetween('[', ']', out var section))
            {
                var optional = view.Consume('?');
                var minimumLength = optional ? 0 : 1;
                // Parse character range expressions
                expression = new Range(c => true, optional, minimumLength);
                return true;
            }

            expression = default;
            return false;
        }
    }
}