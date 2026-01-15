namespace Poly.Text.Matching.Expressions {
    public partial class Group : Expression {
        public Group(Expression[] members, bool optional = false, int minimumLength = 0)
             : base(optional, minimumLength)
        {
            Members = members;
        }

        public Expression[] Members { get; }

        public override void Link(Expression? previous, Expression? next)
        {
            Linker.Link(Members, previous, next);
            base.Link(previous, next);
        }

        public static bool Parse(StringView view, out Expression? expression)
        {
            if (view.ExtractBetween('(', ')', out var section)) {
                var optional = view.Consume('?');
                var minimumLength = optional ? 0 : 1;

                if (Parser.TryParse(section, out Expression[]? members) && members is not null) {
                    expression = new Group(members, optional, minimumLength);
                    return true;
                }
            }

            expression = default;
            return false;
        }
    }
}