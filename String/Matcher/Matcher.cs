using System.Text;

namespace Poly {

    using Data;
    using Poly.String.Matcher;

    public class Matcher {
        private ExtractDelegate Extracter;
        private TemplateDelegate Templater;

        public string Format;

        public Matcher(string format) {
            if (string.IsNullOrEmpty(format)) {
                Extracter = (it, set) => it.IsDone;
                Templater = (it, get) => true;
            }
            else {
                Format = format;
                Parser.Parse(format, out _, out Extracter, out Templater);
            }
        }

        public bool Compare(string data) => Extract(data, (_, __) => true);

        public bool Compare(StringIterator it) => Extract(it, (_, __) => true);

        public bool Extract(string data, out JSON storage) {
            storage = new JSON();

            return Extract(data, storage.Set);
        }

        public bool Extract(string data, SetDelegate set) =>
            Extracter(new StringIterator(data), set);

        public bool Extract(StringIterator it, SetDelegate set) =>
            Extracter(it, set);

        public bool Extract<T>(string data, out T result) {
            return Extract<T>(data, out result, Serializer.GetCached<T>());
        }

        public bool Extract<T>(string data, out T result, Serializer<T> serializer) {
            result = (T)serializer.CreateInstance();
            var set = SetMemberValue<T>(serializer, result);

            return Extract(data, set);
        }

        public bool Extract<T>(StringIterator it, out T result) {
            return Extract(it, out result, Serializer.GetCached<T>());
        }

        public bool Extract<T>(StringIterator it, out T result, Serializer<T> serializer) {
            result = (T)serializer.CreateInstance();
            var set = SetMemberValue(serializer, result);

            return Extracter(it, set);
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

        public bool Template<T>(T storage, out string result) {
            return Template<T>(storage, out result, Serializer.GetCached<T>());
        }

        public bool Template<T>(T storage, out string result, Serializer<T> serializer) {
            return Template(GetMemberValue(serializer, storage), out result);
        }

        public bool Template<T>(StringBuilder text, T storage) {
            return Templater(text, GetMemberValue(Serializer.GetCached<T>(), storage));
        }

        public bool Template<T>(StringBuilder text, T storage, Serializer<T> serializer) {
            return Templater(text, GetMemberValue(serializer, storage));
        }

        public static GetDelegate GetMemberValue<T>(T storage) {
            var serializer = Serializer.GetCached<T>();

            return (string key, out object value) => {
                return serializer.GetMemberValue(storage, key, out value);
            };
        }

        public static SetDelegate SetMemberValue<T>(T storage) {
            var serializer = Serializer.GetCached<T>();

            return (string key, object value) => {
                return serializer.SetMemberValue(storage, key, value);
            };
        }

        public static GetDelegate GetMemberValue<T>(Serializer serializer, T storage) {
            return (string key, out object value) => {
                return serializer.GetMemberValue(storage, key, out value);
            };
        }

        public static SetDelegate SetMemberValue<T>(Serializer serializer, T storage) {
            return (string key, object value) => {
                return serializer.SetMemberValue(storage, key, value);
            };
        }
    }
}