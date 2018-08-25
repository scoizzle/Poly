using System.Text;

namespace Poly.String {
    using Data;

    public class Matcher : Matcher<JSON> {
        public Matcher(string format) : base(format) { }

        protected override bool Parse(
            string text, 
            out CompareDelegate compare, 
            out ExtractDelegate extract, 
            out RawExtractDelegate raw_extract, 
            out TemplateDelegate template,
            out RawTemplateDelegate raw_template,
            out int min_length
        ) {
            var context = new JSONContext(TypeInfo);

            if (Parse(text, context)) {
                var delegates = context.Pop();

                compare = delegates.Compare;
                extract = delegates.Extract;
                raw_extract = delegates.RawExtract;
                template = delegates.Template;
                raw_template = delegates.RawTemplate;
                min_length = context.MinimumLength;
                return true;
            }

            compare = default;
            extract = default;
            raw_extract = default;
            template = default;
            raw_template = default;
            min_length = int.MaxValue;
            return false;
        }

        protected class JSONContext : Context {
            public JSONContext(TypeInformation type) : base(type) { }

            public override bool GetMember(string name, out Member member) {
                member = new Member(
                    name, 
                    typeof(string),
                    (obj) => obj is JSON js ? js.Get(name) : default,
                    (obj, val) => { if (obj is JSON js) js.Set(name, val); },
                    RawStringSerializer.Singleton
                );

                return true;
            }
        }
    }
}