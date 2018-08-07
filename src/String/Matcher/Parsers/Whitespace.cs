namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        static class Whitespace {

            private static CompareDelegate Compare(CompareDelegate next) =>
                (it) =>
                    it.Consume(char.IsWhiteSpace) && next(it);
                    
            private static CompareDelegate CompareOptional(CompareDelegate next) =>
                (it) =>
                    it.Consume(char.IsWhiteSpace) || next(it);
            private static CompareDelegate GotoCompare(CompareDelegate next) =>
                (it) =>
                    it.ConsumeUntil(char.IsWhiteSpace) && 
                    it.Consume(char.IsWhiteSpace) && 
                    next(it);
                    
            private static CompareDelegate GotoCompareOptional(CompareDelegate next) =>
                (it) =>
                    (it.ConsumeUntil(char.IsWhiteSpace) && it.Consume(char.IsWhiteSpace)) |
                    next(it);
                    
            private static ExtractDelegate Extract(ExtractDelegate next) =>
                (it, obj) =>
                    it.Consume(char.IsWhiteSpace) && next(it, obj);

            private static RawExtractDelegate RawExtract(RawExtractDelegate next) =>
                (it, set) =>
                    it.Consume(char.IsWhiteSpace) && next(it, set);
                    
            private static ExtractDelegate ExtractOptional(ExtractDelegate next) =>
                (it, obj) =>
                    it.Consume(char.IsWhiteSpace) | next(it, obj);

            private static RawExtractDelegate RawExtractOptional(RawExtractDelegate next) =>
                (it, set) =>
                    it.Consume(char.IsWhiteSpace) | next(it, set);

            private static ExtractDelegate GotoExtract(ExtractDelegate next) =>
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

            private static RawExtractDelegate GotoRawExtract(RawExtractDelegate next) =>
                (it, set) => {
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
                    
            private static ExtractDelegate GotoExtractOptional(ExtractDelegate next) =>
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

            private static RawExtractDelegate GotoRawExtractOptional(RawExtractDelegate next) =>
                (it, set) => {
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

            private static TemplateDelegate Template(TemplateDelegate next) =>
                (it, obj) => {
                    it.Append(' ');
                    return next(it, obj);
                };

            private static RawTemplateDelegate RawTemplate(RawTemplateDelegate next) =>
                (it, get) => {
                    it.Append(' ');
                    return next(it, get);
                };

            public static bool Parse(StringIterator it, Context context) {
                if (it.Consume('^')) {
                    var is_optional = it.Consume('?');

                    var next = context.Peek();
                    var has_next = Matcher<T>.Parse(it, context);

                    if (has_next)
                        next = context.Peek();

                    if (is_optional) {
                        context.Push((
                            CompareOptional(next.Compare),
                            GotoCompareOptional(next.Compare),
                            ExtractOptional(next.Extract),
                            GotoExtractOptional(next.Extract),
                            RawExtractOptional(next.RawExtract),
                            GotoRawExtractOptional(next.RawExtract),
                            Template(next.Template),
                            RawTemplate(next.RawTemplate)
                        ));
                    }
                    else {
                        context.MinimumLength++;

                        context.Push((
                            Compare(next.Compare),
                            GotoCompare(next.Compare),
                            Extract(next.Extract),
                            GotoExtract(next.Extract),
                            RawExtract(next.RawExtract),
                            GotoRawExtract(next.RawExtract),
                            Template(next.Template),
                            RawTemplate(next.RawTemplate)
                        ));
                    }

                    return true;
                }

                return false;
            }
        }
    }
}