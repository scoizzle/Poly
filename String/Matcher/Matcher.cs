using System.Text;

namespace Poly {
    using Data;
    using Poly.String.Matcher;

	public class Matcher {

        ExtractDelegate Extracter;
        TemplateDelegate Templater;

        public string Format;

        public Matcher(string format) {
            Format = format;
            Parser.Parse(format, out _, out Extracter, out Templater);
        }

        public bool Compare(string data) => Extract(data, (_, __) => true);

        public bool Extract(string data, SetDelegate set) {
            var it = new StringIterator(data);

            if (Extracter(it, set)) {
                it.ConsumeSection();
                return it.IsDone;
            }

            return false;
        }

        public bool Extract(string data, out JSON storage) {
            storage = new JSON();

            return Extract(data, storage.Set);
        }

        public bool Template(GetDelegate get, out string result) {
            var output = new StringBuilder();
            if (Templater(output, get)) {
                result = output.ToString();
                return true;
            }
            result = null;
            return false;
        }

        public bool Template(JSON storage, out string result) {
            return Template(storage.TryGet, out result);
        }

        public bool Extract<T>(string data, out T result) {
            return Extract<T>(data, out result, Serializer.GetCached<T>());
        }

        public bool Extract<T>(string data, out T result, Serializer<T> serializer) {
            result = (T)serializer.CreateInstance();
            var set = SetMemberValue<T>(serializer, result);

            return Extract(data, set);
        }

        public bool Template<T>(T storage, out string result) {
            return Template<T>(storage, out result, Serializer.GetCached<T>());
        }

        public bool Template<T>(T storage, out string result, Serializer<T> serializer) {
            return Template(GetMemberValue(serializer, storage), out result);
        }

        GetDelegate GetMemberValue<T>(Serializer serializer, T storage) {
            return (string key, out object value) => {
                return serializer.GetMemberValue(storage, key, out value);
            };
        }

        SetDelegate SetMemberValue<T>(Serializer serializer, T storage) {
            return (string key, object value) => {
                return serializer.SetMemberValue(storage, key, value);
            };
        }
    }
}
