using System.Text;

namespace Poly.String.Matcher.Parsers {
    public delegate bool GetDelegate(string key, out object value);

    public delegate bool SetDelegate(string key, object value);

    public delegate bool ExtractDelegate(StringIterator it, SetDelegate set);

    public delegate bool TemplateDelegate(StringBuilder it, GetDelegate get);
}

namespace Poly {
    using Data;

    public partial class Matcher {

        public delegate bool _CompareDelegate(StringIterator it);

        public delegate bool _ExtractDelegate(StringIterator it, object obj);

        public delegate bool _TemplateDelegate(StringBuilder it, object obj);
    }
}