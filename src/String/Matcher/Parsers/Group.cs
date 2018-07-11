using System.Text;

namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        static class Group {                
            private static CompareDelegate CompareOptional(CompareDelegate group, CompareDelegate next) =>
                (it) =>
                    group(it) || next(it);
                            
            private static ExtractDelegate ExtractOptional(ExtractDelegate group, ExtractDelegate next) =>
                (it, obj) => 
                    group(it, obj) || next(it, obj);

            private static RawExtractDelegate RawExtractOptional(RawExtractDelegate group, RawExtractDelegate next) =>
                (it, set) => 
                    group(it, set) || next(it, set);
                    
            private static TemplateDelegate TemplateOptional(TemplateDelegate group, TemplateDelegate next) =>
                (it, obj) => {
                    var sub = new StringBuilder();

                    if (group(sub, obj))
                        it.Append(sub);

                    return next(it, obj);
                };

            private static RawTemplateDelegate RawTemplateOptional(RawTemplateDelegate group, RawTemplateDelegate next) =>
                (it, get) => {
                    var sub = new StringBuilder();

                    if (group(sub, get))
                        it.Append(sub);

                    return next(it, get);
                };

            public static bool Parse(StringIterator it, Context context) {
                if (it.SelectSection('(', ')')) {
                    it.ConsumeSection(out int index, out int last_index);

                    var is_optional = it.Consume('?');

                    var next = context.Peek();
                    var min_length = context.MinimumLength;
                    var has_next = Matcher<T>.Parse(it, context);

                    if (has_next)
                        next = context.Peek();

                    it.SelectSection(index, last_index);

                    var parse_group = Matcher<T>.Parse(it, context);
                    if (!parse_group)
                        return false;

                    var group = context.Peek();

                    if (is_optional) {
                        context.MinimumLength = min_length;
                        
                        context.Push((
                            CompareOptional(group.Compare, next.Compare),
                            CompareOptional(group.GotoCompare, next.GotoCompare),
                            ExtractOptional(group.Extract, next.Extract),
                            ExtractOptional(group.GotoExtract, next.GotoExtract),
                            RawExtractOptional(group.RawExtract, next.RawExtract),
                            RawExtractOptional(group.GotoRawExtract, next.GotoRawExtract),
                            TemplateOptional(group.Template, next.Template),
                            RawTemplateOptional(group.RawTemplate, next.RawTemplate)
                        ));
                    }
                    
                    return true;
                }

                return false;
            }
        }
    }
}