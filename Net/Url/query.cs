using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Poly;

namespace Poly.Net {
    using Data;

    public class UrlQuery : KeyValueCollection<string> {
        static Serializer<UrlQuery> Serializer = new Serializer<UrlQuery>(
            Serialize,
            Deserialize
            );

        static bool Serialize(StringBuilder it, UrlQuery query) {
            var pairs = query.KeyValuePairs.ToArray();
            var last = pairs.Length - 1;

            for (var i = 0; i <= last; i++) {
                var pair = pairs[i];

                var key = Uri.EscapeDataString(pair.Key);
                var value = Uri.EscapeDataString(pair.Value);

                it.Append(key).Append('=').Append(value);

                if (i != last) {
                    it.Append('&');
                }
            }

            return true;
        }


        static bool Deserialize(StringIterator it, out UrlQuery query) {
            query = new UrlQuery();

            while (it.SelectSection('=')) {
                var key = it;
                it.ConsumeSection();
                it.SelectSection('&');

                var value = it;

                query.Set(key, value);

                if (it.IsDone)
                    return true;
            }

            query = null;
            return false;
        }

        public static bool Parse(string text, out UrlQuery query) {
            return Deserialize(text, out query);
        }

        public override string ToString() {
            return Serializer.Serialize(this);
        }
    }
}
