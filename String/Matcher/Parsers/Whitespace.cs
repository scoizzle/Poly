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

        private static Matcher.TemplateDelegate Template = (it, get) => {
            it.Append(' ');
            return true;
        };

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out Matcher.TemplateDelegate template) {
            if (it.Consume('^')) {
                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out Matcher.TemplateDelegate next_template);

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
            out Matcher.TemplateDelegate template,
            ExtractDelegate goto_next,
            ExtractDelegate extract_next,
            Matcher.TemplateDelegate template_next) {
            if (it.Consume('^')) {
                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out Matcher.TemplateDelegate next_template,
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