namespace Poly.String.Matcher.Parsers {

    public static class Wildcard {
        private static ExtractDelegate Handler = (it, set) => true;

        private static TemplateDelegate Template = (it, get) => true;

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out TemplateDelegate template) {
            if (it.Consume('*')) {
                var has_next = Parser.Parse(
                    it,
                    out go_to,
                    out _,
                    out template);

                if (has_next) {
                    extract = go_to;
                }
                else {
                    go_to = extract = Handler;
                    template = Template;
                }

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
            if (it.Consume('*')) {
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
                    go_to = next_goto;
                    extract = next_goto;
                    template = next_template;
                }
                else {
                    go_to = goto_next;
                    extract = extract_next;
                    template = template_next;
                }

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
        static class _Wildcard {
            public static bool Parse(StringIterator it, Context context) {
                if (it.Consume('*')) {
                    var next = context.Peek();
                    var has_next = Matcher.Parse(it, context);

                    if (has_next)
                        next = context.Peek();

                    context.Push(next.GotoCompare, next.GotoExtract, next.Template);

                    return true;
                }

                return false;
            }
        }
    }
}