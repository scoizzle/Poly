namespace Poly.String.Matcher {
    using Parsers;

    public static class Parser {
        public static bool Parse(
            StringIterator it, 
            out ExtractDelegate go_to, 
            out ExtractDelegate extract, 
            out TemplateDelegate template) => 
                   Extract.Parse(it, out go_to, out extract, out template) ||
                     Group.Parse(it, out go_to, out extract, out template) ||
                Whitespace.Parse(it, out go_to, out extract, out template) ||
                  Wildcard.Parse(it, out go_to, out extract, out template) ||
                    Static.Parse(it, out go_to, out extract, out template);

        public static bool Parse(
            StringIterator it,
            out ExtractDelegate go_to,
            out ExtractDelegate extract,
            out TemplateDelegate template,
            ExtractDelegate next_goto,
            ExtractDelegate next_extract,
            TemplateDelegate next_template) => 
                   Extract.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
                     Group.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
                Whitespace.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
                  Wildcard.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template) ||
                    Static.Parse(it, out go_to, out extract, out template, next_goto, next_extract, next_template);
    }
}