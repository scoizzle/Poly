using System;
using System.Collections.Generic;
using System.Text;

namespace Poly.String.Matcher {
    public delegate bool GetDelegate(string key, out object value);
    public delegate bool SetDelegate(string key, object value);

    public delegate bool ExtractDelegate(StringIterator it, SetDelegate set);
    public delegate bool TemplateDelegate(StringBuilder it, GetDelegate get);
}
