using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net {
    using Collections;
    using Data;

    public class UrlQuery : Dictionary<string, string> {

        public static Serializer<UrlQuery> Serializer = new Serializer<UrlQuery>(
            Serialize,
            Deserialize
            );

        private static bool Serialize(StringBuilder it, UrlQuery query) {
            using (var enumerator = query.GetEnumerator()) {
                if (!enumerator.MoveNext())
                    return false;

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

        private static bool Deserialize(StringIterator it, out UrlQuery query) {
            query = new UrlQuery();

            while (it.SelectSection('=')) {
                string key = it;
                it.ConsumeSection();
                it.SelectSection('&');

                string value = it;
                it.ConsumeSection();

                query[key] = value;

                if (it.IsDone)
                    return true;
            }

            query = null;
            return false;
        }

        public static UrlQuery Parse(string text) {
            return Serializer.Deserialize(text);
        }

        public override string ToString() {
            return Serializer.Serialize(this);
        }
    }
}