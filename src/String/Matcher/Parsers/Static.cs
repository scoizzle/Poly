using System;

namespace Poly.String.Matcher.Parsers {

    public static class Static {

        private static ExtractDelegate Goto(string text) {
            return (it, get) => {
                var offset = text.Length;

                var start = it.Index;
                var found = it.Goto(text);

                if (!found)
                    return false;

                var stop = it.Index;
                it.Consume(offset);

                it.Index = start;
                it.LastIndex = stop;
                return true;
            };
        }

        private static ExtractDelegate Goto(string text, ExtractDelegate next) {
            return (it, set) => {
                var offset = text.Length;

                var start = it.Index;
                var found = it.Goto(text);

                if (!found)
                    return false;

                var stop = it.Index;
                it.Consume(offset);

                if (next(it, set)) {
                    it.Index = start;
                    it.LastIndex = stop;
                    return true;
                }

                it.Index = start;
                return false;
            };
        }

        private static ExtractDelegate GotoOptional(string text) {
            return (it, get) => {
                var offset = text.Length;

                var start = it.Index;
                var found = it.Goto(text);
                var stop = it.Index;

                if (found)
                    it.Consume(offset);

                it.Index = start;
                it.LastIndex = stop;
                return true;
            };
        }

        private static ExtractDelegate GotoOptional(string text, ExtractDelegate next) {
            return (it, set) => {
                var offset = text.Length;

                var start = it.Index;
                var found = it.Goto(text);
                var stop = it.Index;

                if (found)
                    it.Consume(offset);

                if (next(it, set)) {
                    it.Index = start;
                    it.LastIndex = stop;
                    return true;
                }

                it.Index = start;
                return false;
            };
        }

        private static ExtractDelegate Extract(string text) {
            return (it, set) => {
                var start = it.Index;
                var found = it.Consume(text);

                if (!found || !it.IsDone)
                    return false;

                it.Index = start;
                return true;
            };
        }

        private static ExtractDelegate Extract(string text, ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;
                var found = it.Consume(text);
                var stop = it.Index;

                if (!found)
                    return false;

                if (next(it, set)) {
                    return true;
                }

                it.Index = start;
                return false;
            };
        }

        private static ExtractDelegate ExtractOptional(string text) {
            return (it, get) => (it.Consume(text) && it.IsDone) || true;
        }

        private static ExtractDelegate ExtractOptional(string text, ExtractDelegate next) {
            return (it, set) => {
                var start = it.Index;
                var found = it.Consume(text);
                var stop = it.Index;

                return next(it, set);
            };
        }

        private static TemplateDelegate Templater(string text) {
            return (it, get) => {
                it.Append(text);
                return true;
            };
        }

        private static TemplateDelegate Templater(string text, TemplateDelegate next) {
            return (it, get) => {
                it.Append(text);
                return next(it, get);
            };
        }

        private static void SelectStatic(StringIterator it) {
            while (!it.IsDone) {
                if (it.ConsumeUntil(IsToken)) {
                    if (it.Previous == '\\')
                        continue;
                }

                break;
            }
        }

        private static bool IsToken(char c) {
            switch (c) {
                case '*':
                case '^':
                case '{':
                case '(':
                    return true;
            }

            return false;
        }

        public static bool Parse(StringIterator it, out ExtractDelegate go_to, out ExtractDelegate extract, out TemplateDelegate template) {
            if (it.SelectSection(_ => SelectStatic(_))) {
                var text = it.ToString().Descape();
                it.ConsumeSection();

                var optional = it.Consume('?');

                var has_next = Parser.Parse(
                    it,
                    out ExtractDelegate next_goto,
                    out ExtractDelegate next_extract,
                    out TemplateDelegate next_template);

                if (optional) {
                    if (has_next) {
                        go_to = GotoOptional(text, next_extract);
                        extract = ExtractOptional(text, next_extract);
                        template = Templater(text, next_template);
                    }
                    else {
                        go_to = GotoOptional(text);
                        extract = ExtractOptional(text);
                        template = Templater(text);
                    }
                }
                else {
                    if (has_next) {
                        go_to = Goto(text, next_extract);
                        extract = Extract(text, next_extract);
                        template = Templater(text, next_template);
                    }
                    else {
                        go_to = Goto(text);
                        extract = Extract(text);
                        template = Templater(text);
                    }
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
            if (it.SelectSection(_ => SelectStatic(_))) {
                var text = it.ToString().Descape();
                it.ConsumeSection();

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
                        go_to = GotoOptional(text, next_extract);
                        extract = ExtractOptional(text, next_extract);
                        template = Templater(text, next_template);
                    }
                    else {
                        go_to = Goto(text, next_extract);
                        extract = Extract(text, next_extract);
                        template = Templater(text, next_template);
                    }
                }
                else {
                    if (optional) {
                        go_to = GotoOptional(text, goto_next);
                        extract = ExtractOptional(text, extract_next);
                        template = Templater(text, template_next);
                    }
                    else {
                        go_to = Goto(text, goto_next);
                        extract = Extract(text, extract_next);
                        template = Templater(text, template_next);
                    }
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
        static class _Static {                
            private static _CompareDelegate Compare(string text, _CompareDelegate next) =>
                (it) => {
                    var start = it.Index;

                    if (!it.Consume(text))
                        return false;

                    if (next(it))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static _CompareDelegate CompareOptional(string text, _CompareDelegate next) =>
                (it) => {
                    var start = it.Index;
                    it.Consume(text);

                    if (next(it))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static _CompareDelegate GotoCompare(string text, _CompareDelegate next) =>
                (it) => {
                    var start = it.Index;

                    if (!it.Goto(text))
                        return false;
                
                    var stop = it.Index;
                    it.Consume(text.Length);

                    if (next(it)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    return false;
                };
                    
            private static _CompareDelegate GotoCompareOptional(string text, _CompareDelegate next) =>
                (it) => {
                    var start = it.Index;
                    var found = it.Goto(text);
                    var stop = it.Index;

                    if (found)
                        it.Consume(text.Length);

                    if (next(it)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    return false;
                };

            private static _ExtractDelegate Extract(string text, _ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;

                    if (!it.Consume(text))
                        return false;

                    if (next(it, obj))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static _ExtractDelegate ExtractOptional(string text, _ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;
                    it.Consume(text);

                    if (next(it, obj))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static _ExtractDelegate GotoExtract(string text, _ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;

                    if (!it.Goto(text))
                        return false;
                
                    var stop = it.Index;
                    it.Consume(text.Length);

                    if (next(it, obj)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    it.LastIndex = stop;
                    return false;
                };
                    
            private static _ExtractDelegate GotoExtractOptional(string text, _ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;
                    var found = it.Goto(text);
                    var stop = it.Index;

                    if (found)
                        it.Consume(text.Length);

                    if (next(it, obj)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    it.LastIndex = stop;
                    return false;
                };     

            private static _TemplateDelegate Template(string text, _TemplateDelegate next) =>
                (it, obj) => {
                    it.Append(text);
                    return next(it, obj);
                };

            private static void SelectStatic(StringIterator it) {
                while (!it.IsDone) {
                    if (it.ConsumeUntil(IsToken)) {
                        if (it.Previous == '\\') {
                            it.Consume();
                            continue;
                        }
                    }

                    break;
                }
            }

            private static bool IsToken(char c) {
                switch (c) {
                    case '*':
                    case '^':
                    case '{':
                    case '(':
                    case '?':
                        return true;
                }

                return false;
            }

            public static bool Parse(StringIterator it, Context context) {
                if (it.SelectSection(_ => SelectStatic(_))) {
                    it.ConsumeSection(out string text);
                    text = text.Descape();

                    var is_optional = it.Consume('?');

                    var next = context.Peek();
                    var has_next = Matcher.Parse(it, context);

                    if (has_next) 
                        next = context.Peek();

                    if (is_optional) {
                        context.Push((
                            CompareOptional(text, next.Compare),
                            GotoCompareOptional(text, next.Compare),
                            ExtractOptional(text, next.Extract),
                            GotoExtractOptional(text, next.Extract),
                            Template(text, next.Template)
                        ));
                    }
                    else {
                        context.MinimumLength += text.Length;
                        
                        context.Push((
                            Compare(text, next.Compare),
                            GotoCompare(text, next.Compare),
                            Extract(text, next.Extract),
                            GotoExtract(text, next.Extract),
                            Template(text, next.Template)
                        ));
                    }

                    return true;
                }

                return false;
            }
        }
    }
}