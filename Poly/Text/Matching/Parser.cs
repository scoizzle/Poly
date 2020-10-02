using System;
using System.Linq;
using System.Collections.Generic;

namespace Poly.Text.Matching {
    using Expressions;

    public static class Parser {
        public static bool TryParse(StringView view, out Expression group) {
            if (TryParse(view, out Expression[] expressions)) {
                var minimumLength = expressions.Sum(exp => exp.MinimumLength);

                group = new Group(members: expressions,
                                    optional: false,
                                    minimumLength: minimumLength);

                group.Link(default, default);
                return true;
            }

            group = default;
            return false;
        }

        public static bool TryParse(StringView view, out Expression[] expressions) {
            var list = new List<Expression>();

            while (!view.IsDone) {
                if (Parse(view, out var expression)) {
                    list.Add(expression);
                }
                else {
                    expressions = default;
                    return false;
                }
            }

            expressions = list.ToArray();
            return true;
        }

        private static bool Parse(StringView view, out Expression expression)
            => Whitespace.Parse(view, out expression) ||
               Wildcard.Parse(view, out expression)   ||
               Extraction.Parse(view, out expression) ||
               Group.Parse(view, out expression)      ||
               Range.Parse(view, out expression)      ||
               Static.Parse(view, out expression);
    }
}