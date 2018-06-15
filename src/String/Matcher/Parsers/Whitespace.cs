namespace Poly.String.Matcher.Parsers {

    public static class Whitespace {

        private static ExtractDelegate Goto() {
            return (it, set) => {
                var start = it.Index;
                var found = it.ConsumeUntil(char.IsWhiteSpace);

                if (!found)
                    return false;

                var stop = it.Index;
                it.Consume(char.IsWhiteSpace);

                it.Index = start;
                it.LastIndex = stop;
                return true;
            };
        }

        private static ExtractDelegate Goto(ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;
                var found = it.ConsumeUntil(char.IsWhiteSpace);

                if (!found)
                    return false;

                var stop = it.Index;
                it.Consume(char.IsWhiteSpace);

                if (next(it, set)) {
                    it.Index = start;
                    it.LastIndex = stop;
                    return true;
                }

                it.Index = start;
                return false;
            };
        }

        private static ExtractDelegate GotoOptional() {
            return (it, set) => {
                var start = it.Index;
                var found = it.ConsumeUntil(char.IsWhiteSpace);

                if (found) {
                    var stop = it.Index;

                    it.Consume(char.IsWhiteSpace);

                    it.Index = start;
                    it.LastIndex = stop;
                }

                return true;
            };
        }

        private static ExtractDelegate GotoOptional(ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;
                var found = it.ConsumeUntil(char.IsWhiteSpace);
                var stop = it.Index;

                if (found)
                    it.Consume(char.IsWhiteSpace);

                if (next(it, set)) {
                    it.Index = start;
                    it.LastIndex = stop;
                    return true;
                }

                it.Index = start;
                return false;
            };
        }

        private static ExtractDelegate Extract() {
            return (it, get) => it.Consume(char.IsWhiteSpace);
        }

        private static ExtractDelegate Extract(ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;
                var found = it.Consume(char.IsWhiteSpace);

                if (!found)
                    return false;

                if (next(it, set))
                    return true;

                it.Index = start;
                return false;
            };
        }

        private static ExtractDelegate ExtractOptional() {
            return (it, get) => it.Consume(char.IsWhiteSpace) || true;
        }

        private static ExtractDelegate ExtractOptional(ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;
                var found = it.Consume(char.IsWhiteSpace);

                if (next(it, set))
                    return true;

                it.Index = start;
                return false;
            };
        }

        private static TemplateDelegate Template = (it, get) => {
            it.Append(' ');
            return true;
        };

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out TemplateDelegate template) {
            if (it.Consume('^')) {
                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out TemplateDelegate next_template);

                var optional = it.Consume('?');

                if (optional) {
                    if (has_next) {
                        go_to = GotoOptional(next_extract);
                        extract = ExtractOptional(next_extract);
                    }
                    else {
                        go_to = GotoOptional();
                        extract = ExtractOptional();
                    }
                }
                else {
                    if (has_next) {
                        go_to = Goto(next_extract);
                        extract = Extract(next_extract);
                    }
                    else {
                        go_to = Goto();
                        extract = Extract();
                    }
                }

                template = Template;
                return true;
            }

            go_to = null;
            extract = null;
            template = null;
            return false;
        }

        public static bool Parse(
            StringIterator it,
            out ExtractDelegate go_to,
            out ExtractDelegate extract,
            out TemplateDelegate template,
            ExtractDelegate goto_next,
            ExtractDelegate extract_next,
            TemplateDelegate template_next) {
            if (it.Consume('^')) {
                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out TemplateDelegate next_template,
                    goto_next,
                    extract_next,
                    template_next
                    );

                if (has_next) {
                    if (optional) {
                        go_to = GotoOptional(next_extract);
                        extract = ExtractOptional(next_extract);
                    }
                    else {
                        go_to = Goto(next_extract);
                        extract = Extract(next_extract);
                    }
                }
                else {
                    if (optional) {
                        go_to = GotoOptional(extract_next);
                        extract = ExtractOptional(extract_next);
                    }
                    else {
                        go_to = Goto(extract_next);
                        extract = Extract(extract_next);
                    }
                }

                template = Template;
                return true;
            }

            go_to = null;
            extract = null;
            template = null;
            return false;
        }
    }
}


namespace Poly {
    using Data;

    public partial class Matcher {
        static class _Whitespace {

            private static _CompareDelegate Compare(_CompareDelegate next) =>
                (it) =>
                    it.Consume(char.IsWhiteSpace) && next(it);
                    
            private static _CompareDelegate CompareOptional(_CompareDelegate next) =>
                (it) =>
                    it.Consume(char.IsWhiteSpace) || next(it);
            private static _CompareDelegate GotoCompare(_CompareDelegate next) =>
                (it) =>
                    it.ConsumeUntil(char.IsWhiteSpace) && 
                    it.Consume(char.IsWhiteSpace) && 
                    next(it);
                    
            private static _CompareDelegate GotoCompareOptional(_CompareDelegate next) =>
                (it) =>
                    (it.ConsumeUntil(char.IsWhiteSpace) && it.Consume(char.IsWhiteSpace)) |
                    next(it);
                    
            private static _ExtractDelegate Extract(_ExtractDelegate next) =>
                (it, obj) =>
                    it.Consume(char.IsWhiteSpace) && next(it, obj);
                    
            private static _ExtractDelegate ExtractOptional(_ExtractDelegate next) =>
                (it, obj) =>
                    it.Consume(char.IsWhiteSpace) | next(it, obj);

            private static _ExtractDelegate GotoExtract(_ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;
                    var found = it.ConsumeUntil(char.IsWhiteSpace);

                    if (!found)
                        return false;

                    var stop = it.Index;
                    it.Consume(char.IsWhiteSpace);

                    if (next(it, obj)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    return false;
                };
                    
            private static _ExtractDelegate GotoExtractOptional(_ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;
                    var found = it.ConsumeUntil(char.IsWhiteSpace);
                    var stop = it.Index;

                    if (found)
                        it.Consume(char.IsWhiteSpace);

                    if (next(it, obj)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    return false;
                };

            private static _TemplateDelegate Template(_TemplateDelegate next) =>
                (it, obj) => {
                    it.Append(' ');
                    return next(it, obj);
                };

            public static bool Parse(StringIterator it, Context context) {
                if (it.Consume('^')) {
                    var is_optional = it.Consume('?');

                    var next = context.Peek();
                    var has_next = Matcher.Parse(it, context);

                    if (has_next)
                        next = context.Peek();

                    if (is_optional) {
                        context.Push((
                            CompareOptional(next.Compare),
                            GotoCompareOptional(next.Compare),
                            ExtractOptional(next.Extract),
                            GotoExtractOptional(next.Extract),
                            Template(next.Template)
                        ));
                    }
                    else {
                        context.MinimumLength++;

                        context.Push((
                            Compare(next.Compare),
                            GotoCompare(next.Compare),
                            Extract(next.Extract),
                            GotoExtract(next.Extract),
                            Template(next.Template)
                        ));
                    }

                    return true;
                }

                return false;
            }
        }
    }
}