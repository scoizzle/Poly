using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        static class Extraction {
            private static CompareDelegate Compare(Serializer serializer, CompareDelegate next) =>
                (it) =>
                    next(it) &&
                    serializer.ValidateFormat(it);
                    
            private static CompareDelegate CompareOptional(Serializer serializer, CompareDelegate next) =>
                (it) =>
                    next(it) &&
                    serializer.ValidateFormat(it) || true;
                            
            private static ExtractDelegate Extract(Serializer serializer, Member member, ExtractDelegate next) =>
                (it, obj) => {
                    if (next(it, obj))
                    if (serializer.DeserializeObject(it, out object value)) {
                        member.Set(obj, value);
                        return true;
                    }

                    return false;
                };

            private static RawExtractDelegate RawExtract(string name, RawExtractDelegate next) =>
                (it, set) => 
                    next(it, set) && set(name, it.ToString());
                    
            private static ExtractDelegate ExtractOptional(Serializer serializer, Member member, ExtractDelegate next) =>
                (it, obj) => {
                    if (!next(it, obj))
                        return false;

                    if (serializer.DeserializeObject(it, out object value))
                        member.Set(obj, value);

                    return true;
                };

            private static RawExtractDelegate RawExtractOptional(string name, RawExtractDelegate next) =>
                (it, set) => 
                    next(it, set) && (set(name, it.ToString()) | true);
                    
            private static TemplateDelegate Template(Serializer serializer, Member member, TemplateDelegate next) =>
                (it, obj) => 
                    serializer.SerializeObject(it, member.Get(obj)) && next(it, obj);

            private static RawTemplateDelegate RawTemplate(string name, RawTemplateDelegate next) =>
                (it, get) => {
                    if (!get(name, out object value))
                        return false;

                    it.Append(value);
                    return next(it, get);
                };
                    
            private static TemplateDelegate TemplateOptional(Serializer serializer, Member member, TemplateDelegate next) =>
                (it, obj) => 
                    (serializer.SerializeObject(it, member.Get(obj)) || true) && 
                    next(it, obj);
                    
            private static RawTemplateDelegate RawTemplateOptional(string name, RawTemplateDelegate next) =>
                (it, get) => {
                    if (get(name, out object value))
                        it.Append(value);

                    return next(it, get);
                };

            public static bool Parse(StringIterator it, Context context) {
                if (it.SelectSection('{', '}')) {
                    it.ConsumeSection(out string name);

                    var is_optional = it.Consume('?');
                    var is_member = context.GetMember(name, out Member member);
                    
                    var serializer = member.Type == typeof(string) ? 
                        RawStringSerializer.Singleton : 
                        member.Serializer;

                    var next = context.Peek();
                    var has_next = Matcher<T>.Parse(it, context);

                    if (has_next) 
                        next = context.Peek();

                    if (is_optional) {
                        context.Push(
                            CompareOptional(serializer, next.GotoCompare),
                            ExtractOptional(serializer, member, next.GotoExtract),
                            RawExtractOptional(name, next.GotoRawExtract),
                            TemplateOptional(serializer, member, next.Template),
                            RawTemplateOptional(name, next.RawTemplate)
                        );
                    }
                    else {
                        context.Push(
                            Compare(serializer, next.GotoCompare),
                            Extract(serializer, member, next.GotoExtract),
                            RawExtract(name, next.GotoRawExtract),
                            Template(serializer, member, next.Template),
                            RawTemplate(name, next.RawTemplate)
                        );
                    }
                    
                    return true;
                }

                return false;
            }
        }
    }
}