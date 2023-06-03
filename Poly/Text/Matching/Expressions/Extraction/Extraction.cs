namespace Poly.Text.Matching.Expressions
{
    public partial class Extraction : Expression {
        public Extraction(string name, bool optional = false, int minimumLength = 0)
             : base(optional, minimumLength)
        {
            Name = name;
        }

        public string Name { get; }

        public static bool Parse(StringView view, out Expression expression) {
            if (view.ExtractBetween('{', '}', out var name)) {
                var optional = view.Consume('?');
                var minimumLength = optional ? 0 : 1;

                expression = new Extraction(name.ToString(), optional, minimumLength);
                return true;
            }

            expression = default;
            return false;
        }
    }
}