using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net {
    using Collections;
    using Data;

    public class UrlQuery : Dictionary<string, string> {
        public static UrlQuerySerializer Serializer = new UrlQuerySerializer();

        public static UrlQuery Parse(string text) {
            return Serializer.Deserialize(text);
        }

        public override string ToString() {
            return Serializer.Serialize(this);
        }
    }

    public class UrlQuerySerializer : Serializer<UrlQuery> {
        public override bool Serialize(StringBuilder it, UrlQuery query) {
            using (var enumerator = query.GetEnumerator()) {
                if (!enumerator.MoveNext())
                    return true;

                do {
                    var current = enumerator.Current;
                    it.Append(Uri.EscapeDataString(current.Key))
                      .Append('=')
                      .Append(Uri.EscapeDataString(current.Value));

                    if (enumerator.MoveNext())
                        it.Append('&');
                    else
                        break;
                }
                while (true);

                return true;
            }
        }

        public override bool Deserialize(StringIterator it, out UrlQuery query) {
            query = new UrlQuery();

            it.SelectSplitSections('&');

            while (!it.IsDone) {
                if (!it.SelectSection('='))
                    return false;

                string key = it;
                it.ConsumeSection();

                string value = it;

                query[key] = value;

                if (it.IsLastSection) 
                    break;

                it.ConsumeSection();
            }

            return true;
        }

        public override bool ValidateFormat(StringIterator it) {
            it.SelectSplitSections('&');

            while (!it.IsDone) {
                if (!it.Goto('='))
                    return false;

                if (it.IsLastSection) 
                    break;
                    
                it.ConsumeSection();
            }

            return true;
        }
    }
}