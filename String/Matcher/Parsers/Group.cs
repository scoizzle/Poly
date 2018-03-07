using System.Text;

namespace Poly.String.Matcher.Parsers {

    public static class Group {

        private static ExtractDelegate Handler(ExtractDelegate group) {
            return group;
        }

        private static ExtractDelegate Handler(ExtractDelegate group, ExtractDelegate next) =>
            (it, set) => group(it, set) && next(it, set);

        private static ExtractDelegate OptionalHandler(ExtractDelegate group) =>
            (it, set) => group(it, set) || true;

        private static ExtractDelegate OptionalHandler(ExtractDelegate group, ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;

                if (group(it, set))
                    return true;

                it.Index = start;
                return next(it, set);
            };
        }

        private static Matcher.TemplateDelegate Templater(Matcher.TemplateDelegate group) => group;

        private static Matcher.TemplateDelegate OptionalTemplater(Matcher.TemplateDelegate group) {
            return (it, get) => {
                var sub = new StringBuilder();

                if (group(sub, get))
                    it.Append(sub);

                return true;
            };
        }

        private static Matcher.TemplateDelegate OptionalTemplater(Matcher.TemplateDelegate group, Matcher.TemplateDelegate next) {
            return (it, get) => {
                var sub = new StringBuilder();

                if (group(sub, get)) {
                    it.Append(sub);
                    return true;
                }

                return next(it, get);
            };
        }

        private static bool parse_internal(
            ExtractDelegate group_goto,
            ExtractDelegate group_extract,
            Matcher.TemplateDelegate group_template,
            bool optional,
            out ExtractDelegate go_to,
            out ExtractDelegate extract,
            out Matcher.TemplateDelegate template,
            bool has_next,
            ExtractDelegate next_goto,
            ExtractDelegate next_extract,
            Matcher.TemplateDelegate next_template) {
            if (has_next) {
                if (optional) {
                    go_to = OptionalHandler(group_goto, next_goto);
                    extract = OptionalHandler(group_extract, next_extract);
                    template = OptionalTemplater(group_template, next_template);
                }
                else {
                    go_to = Handler(group_goto, next_goto);
                    extract = Handler(group_extract, next_extract);
                    template = Templater(group_template);
                }
            }
            else {
                if (optional) {
                    go_to = OptionalHandler(group_goto);
                    extract = OptionalHandler(group_extract);
                    template = OptionalTemplater(group_template);
                }
                else {
                    go_to = Handler(next_goto);
                    extract = Handler(next_extract);
                    template = Templater(group_template);
                }
            }

            return true;
        }

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out Matcher.TemplateDelegate template) {
            if (it.SelectSection('(', ')')) {
                var index = it.Index;
                var last_index = it.LastIndex;

                it.ConsumeSection();
                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out Matcher.TemplateDelegate next_template);

                it.Index = index;
                it.LastIndex = last_index;

                if (has_next) {
                    var parse_group = Parser.Parse(
                        it,
                        out ExtractDelegate parsed_goto,
                        out ExtractDelegate parsed_extract,
                        out Matcher.TemplateDelegate parsed_template,
                        next_goto,
                        next_extract,
                        next_template);

                    return parse_internal(
                        parsed_goto,
                        parsed_extract,
                        parsed_template,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        true,
                        next_goto,
                        next_extract,
                        next_template);
                }
                else {
                    var parse_group = Parser.Parse(
                        it,
                        out ExtractDelegate parsed_goto,
                        out ExtractDelegate parsed_extract,
                        out Matcher.TemplateDelegate parsed_template);

                    return parse_internal(
                        parsed_goto,
                        parsed_extract,
                        parsed_template,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        false,
                        null,
                        null,
                        null);
                }
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
            if (it.SelectSection('(', ')')) {
                var index = it.Index;
                var last_index = it.LastIndex;

                it.ConsumeSection();
                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out Matcher.TemplateDelegate next_template,
                    goto_next,
                    extract_next,
                    template_next);

                it.Index = index;
                it.LastIndex = last_index;

                if (has_next) {
                    var parse_group = Parser.Parse(
                        it,
                        out ExtractDelegate parsed_goto,
                        out ExtractDelegate parsed_extract,
                        out Matcher.TemplateDelegate parsed_template,
                        next_goto,
                        next_extract,
                        next_template);

                    return parse_internal(
                        parsed_goto,

                        parsed_extract,
                        parsed_template,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        true,
                        next_goto,
                        next_extract,
                        next_template);
                }
                else {
                    var parse_group = Parser.Parse(
                        it,
                        out ExtractDelegate parsed_goto,
                        out ExtractDelegate parsed_extract,
                        out Matcher.TemplateDelegate parsed_template,
                        goto_next,
                        extract_next,
                        template_next);

                    return parse_internal(
                        parsed_goto,
                        parsed_extract,
                        parsed_template,
                        optional,
                        out go_to,
                        out extract,
                        out template,
                        true,
                        goto_next,
                        extract_next,
                        template_next);
                }
            }

            go_to = null;
            extract = null;
            template = null;
            return false;
        }
    }
}