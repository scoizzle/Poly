using Poly.Data;

namespace Poly.Net {

    public class Url {
        public const string MatchString = "{Scheme}://({Username}(:{Password})?@)?{Hostname}(:{Port as Optional<Int32>})?(/{Path})?(\\?{Query})?(#{Fragment})?";
        public static Matcher Matcher;
        public static Serializer<Url> Serializer;

        static Url() {
            Matcher = new Matcher(MatchString);

            Serializer = new Serializer<Url>();
            Serializer.TryDeserialize = Matcher.Extract;
            Serializer.TrySerialize = Matcher.Template;
        }

        public string Scheme;
        public string Username;
        public string Password;
        public string Hostname;
        public int? Port;
        public string Path;
        public string Query;
        public string Fragment;

        public override string ToString() {
            return Serializer.Serialize(this);
        }

        public static Url Parse(string text) {
            return Serializer.Deserialize(text);
        }
    }
}