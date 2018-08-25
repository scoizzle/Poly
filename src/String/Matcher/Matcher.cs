using System.Text;

namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        protected CompareDelegate comparer;
        protected ExtractDelegate extracter;
        protected TemplateDelegate templater;

        protected RawExtractDelegate raw_extracter;
        protected RawTemplateDelegate raw_templater;

        public readonly string Format;
        public readonly int MinimumLength;
        public readonly TypeInformation TypeInfo;

        public Matcher(string format) {
            Format = format;
            TypeInfo = TypeInformation.Get<T>();

            Parse(format, out comparer, out extracter, out raw_extracter, out templater, out raw_templater, out MinimumLength);
        }

        public bool Compare(string text) => 
            text?.Length > MinimumLength ?
                comparer(text) :
                false;

        public bool Compare(StringIterator it) => 
            it?.Length > MinimumLength ?
                comparer(it) :
                false;

        public bool Extract(string text, T obj) => 
            text?.Length > MinimumLength ?
                extracter(text, obj) :
                false;

        public bool Extract(StringIterator it, T obj) => 
            it?.Length > MinimumLength ?
                extracter(it, obj) :
                false;

        public bool Extract(string text, out T obj) {
            if (text?.Length > MinimumLength) 
                return extracter(text, obj = TypeInfo.CreateInstance<T>());

            obj = default;
            return false;
        }

        public bool Extract(StringIterator it, out T obj) {
            if (it?.Length > MinimumLength) 
                return extracter(it, obj = TypeInfo.CreateInstance<T>());

            obj = default;
            return false;
        }

        public bool Extract(string text, TrySetMemberDelegate set) =>
            text?.Length > MinimumLength ?
                raw_extracter(text, set) :
                false;

        public bool Extract(StringIterator it, TrySetMemberDelegate set) =>
            it?.Length > MinimumLength ?
                raw_extracter(it, set) :
                false;

        public bool Template(StringBuilder output, T obj) =>
            templater(output, obj);

        public bool Template(T obj, out string text) {
            var output = new StringBuilder();

            if (templater(output, obj)) {
                text = output.ToString();
                return true;
            }

            text = default;
            return false;
        }

        public bool Template(StringBuilder output, TryGetMemberDelegate get) =>
            raw_templater(output, get);

        protected virtual bool Parse(
            string text, 
            out CompareDelegate compare, 
            out ExtractDelegate extract, 
            out RawExtractDelegate raw_extract, 
            out TemplateDelegate template,
            out RawTemplateDelegate raw_template,
            out int min_length
        ) {
            var context = new Context(TypeInfo);

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
    }
}