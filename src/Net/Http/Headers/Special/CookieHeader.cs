using System.Collections.Generic;

namespace Poly.Net.Http {
    using Collections;

    public class CookieHeader : Header {
        public Dictionary<string, Cookie> Storage;

        public CookieHeader() : base("Cookie") {
            Storage = new Dictionary<string, Cookie>();
        }

        public Cookie this[string key] {
            get => Storage.TryGetValue(key, out Cookie value) ? value : default;
            set => Storage[key] = value;
        }

        public override IEnumerable<string> Serialize() =>
            Storage.TrySelect(pair => $"{pair.Key}={pair.Value.Value}");

        public override void Deserialize(string value) =>
            TryDeserialize(value);

        public override void Reset() =>
            Storage.Clear();

        public bool TryDeserialize(StringIterator it) {
            it.SelectSplitSections("; ");

            do {
                if (!it.SelectSection('='))
                    goto format_error;

                it.ConsumeSection(out string key);
                it.ConsumeSection(out string value);

                Storage.Add(key, new Cookie { Name = key, Value = value });
            }
            while (!it.IsDone);

            return true;
            
        format_error:
            return false;
        }
    }
}