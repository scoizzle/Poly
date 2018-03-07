using Poly.Data;

namespace Poly.Net {

    public class Url {

        public static Serializer<Url> Serializer = new Serializer<Url>(
            "{Scheme}://({Username}(:{Password})?@)?{Hostname}(:{Port as Optional<Int32>})?(/{Path})?(\\?{Query})?(#{Fragment})?"
            );

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