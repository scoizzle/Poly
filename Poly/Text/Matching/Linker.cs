using Poly.Data;

namespace Poly.Text.Matching
{
    public static class Linker
    {
        public static void Link(Expression[] expressions, Expression? previous = default, Expression? next = default)
        {
            // var it = new ArrayIterator<Expression>(expressions);

            // if (it.IsDone) // expressions was empty;
            // return;

            // if (it.IsLast) { // expressions contains only 1 expression
            // it.Current.Link(previous, next);
            // }
            // else {
            // it.Current.Link(previous, it.Next);

            // while (!it.IsLast && it.MoveNext()) {
            // it.Current.Link(it.Previous, it.Next);
            // }

            // it.Current.Link(it.Previous, next);
            // }
        }
    }
}