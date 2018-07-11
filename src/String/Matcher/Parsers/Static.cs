namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        static class Static {                
            private static CompareDelegate Compare(string text, CompareDelegate next) =>
                (it) => {
                    var start = it.Index;

                    if (!it.Consume(text))
                        return false;

                    if (next(it))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static CompareDelegate CompareOptional(string text, CompareDelegate next) =>
                (it) => {
                    var start = it.Index;
                    it.Consume(text);

                    if (next(it))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static CompareDelegate GotoCompare(string text, CompareDelegate next) =>
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
                    
            private static CompareDelegate GotoCompareOptional(string text, CompareDelegate next) =>
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

            private static ExtractDelegate Extract(string text, ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;

                    if (!it.Consume(text))
                        return false;

                    if (next(it, obj))
                        return true;

                    it.Index = start;
                    return false;
                };

            private static RawExtractDelegate RawExtract(string text, RawExtractDelegate next) =>
                (it, set) => {
                    var start = it.Index;

                    if (!it.Consume(text))
                        return false;

                    if (next(it, set))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static ExtractDelegate ExtractOptional(string text, ExtractDelegate next) =>
                (it, obj) => {
                    var start = it.Index;
                    it.Consume(text);

                    if (next(it, obj))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static RawExtractDelegate RawExtractOptional(string text, RawExtractDelegate next) =>
                (it, set) => {
                    var start = it.Index;
                    it.Consume(text);

                    if (next(it, set))
                        return true;

                    it.Index = start;
                    return false;
                };
                    
            private static ExtractDelegate GotoExtract(string text, ExtractDelegate next) =>
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
                    
            private static RawExtractDelegate GotoRawExtract(string text, RawExtractDelegate next) =>
                (it, set) => {
                    var start = it.Index;

                    if (!it.Goto(text))
                        return false;
                
                    var stop = it.Index;
                    it.Consume(text.Length);

                    if (next(it, set)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    it.LastIndex = stop;
                    return false;
                };
                    
            private static ExtractDelegate GotoExtractOptional(string text, ExtractDelegate next) =>
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

            private static RawExtractDelegate GotoRawExtractOptional(string text, RawExtractDelegate next) =>
                (it, set) => {
                    var start = it.Index;
                    var found = it.Goto(text);
                    var stop = it.Index;

                    if (found)
                        it.Consume(text.Length);

                    if (next(it, set)) {
                        it.Index = start;
                        it.LastIndex = stop;
                        return true;
                    }

                    it.Index = start;
                    it.LastIndex = stop;
                    return false;
                };     
                
            private static TemplateDelegate Template(string text, TemplateDelegate next) =>
                (it, obj) => {
                    it.Append(text);
                    return next(it, obj);
                };

            private static RawTemplateDelegate RawTemplate(string text, RawTemplateDelegate next) =>
                (it, get) => {
                    it.Append(text);
                    return next(it, get);
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
                    var has_next = Matcher<T>.Parse(it, context);

                    if (has_next) 
                        next = context.Peek();

                    if (is_optional) {
                        context.Push((
                            CompareOptional(text, next.Compare),
                            GotoCompareOptional(text, next.Compare),
                            ExtractOptional(text, next.Extract),
                            GotoExtractOptional(text, next.Extract),
                            RawExtractOptional(text, next.RawExtract),
                            GotoRawExtractOptional(text, next.RawExtract),
                            Template(text, next.Template),
                            RawTemplate(text, next.RawTemplate)
                        ));
                    }
                    else {
                        context.MinimumLength += text.Length;
                        
                        context.Push((
                            Compare(text, next.Compare),
                            GotoCompare(text, next.Compare),
                            Extract(text, next.Extract),
                            GotoExtract(text, next.Extract),
                            RawExtract(text, next.RawExtract),
                            GotoRawExtract(text, next.RawExtract),
                            Template(text, next.Template),
                            RawTemplate(text, next.RawTemplate)
                        ));
                    }

                    return true;
                }

                return false;
            }
        }
    }
}