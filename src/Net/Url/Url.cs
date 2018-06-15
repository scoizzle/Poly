﻿using System.Text;

namespace Poly.Net {
    using Data;

    public class Url {
        public string Scheme;
        public string Username;
        public string Password;
        public string Hostname;
        public int Port;
        public string Path;
        public UrlQuery Query;
        public string Fragment;

        public override string ToString() {
            var text = new StringBuilder();
            return UrlSerializer.Matcher.Template(text, this) ? text.ToString() : default;
        }

        public static Url Parse(string text) =>
            UrlSerializer.Matcher.Extract(text, out Url value) ? value : default;
        
        public static bool TryParse(string text, out Url value) =>
            UrlSerializer.Matcher.Extract(text, out value);
    }

    public class UrlSerializer : Serializer<Url> {
        public const string MatchString = 
            "{Scheme}" +
            "://" +
            "(" +
                "{Username}" +
                "(" +
                    ":" +
                    "{Password}" +
                ")?" +
                "@" +
            ")?" +
            "{Hostname}" +
            "(" +
                ":" +
                "{Port}" +
            ")?" +
            "(" +
                "/"+
                "{Path}" +
            ")?" +
            "(" +
                "\\?" +
                "{Query}" +
            ")?" +
            "(" +
                "#" +
                "{Fragment}" +
            ")?";

        public static Matcher Matcher = new Matcher(MatchString);

        public override bool Serialize(StringBuilder json, Url value) =>
            Matcher.Template(json, value);

        public override bool Deserialize(StringIterator json, out Url value) =>
            Matcher.Extract(json, out value);

        public override bool ValidateFormat(StringIterator json) =>
            Matcher.Compare(json);
    }
}