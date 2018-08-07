using System.Text;

namespace Poly.String {
    using Data;

    public partial class Matcher<T> {
        public delegate bool CompareDelegate(StringIterator it);

        public delegate bool ExtractDelegate(StringIterator it, T obj);

        public delegate bool TemplateDelegate(StringBuilder it, T obj);
        
        public delegate bool RawExtractDelegate(StringIterator it, TrySetMemberDelegate set);

        public delegate bool RawTemplateDelegate(StringBuilder it, TryGetMemberDelegate get);
    }
}