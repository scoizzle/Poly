using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Web;

namespace Poly.Script.Libraries {
    using Data;
    using Nodes;

    public class Http : Library {
        public Http() {
            RegisterStaticObject("Http", this);

            Add(Get);
            Add(Post);
            Add(Escape);
            Add(Descape);
            Add(Server);
        }

        new public static Function Get = Function.Create("Get", (string Url, jsObject Headers) => {
            using (var Client = new WebClient()) {
                if (Headers != null) {
                    Headers.ForEach((K, V) => {
                        Client.Headers.Add(K, V.ToString());
                    });
                }

                try {
                    return Client.DownloadString(Url);
                }
                catch { }
            }
            return null;
        });

        public static Function Post = Function.Create("Post", (string Url, jsObject Headers, object RawData) => {
            var Data = RawData is jsObject ?
                (RawData as jsObject).ToPostString() :
                (RawData as string);

            using (var Client = new WebClient()) {
                if (Headers != null) {
                    Headers.ForEach((K, V) => {
                        Client.Headers.Add(K, V.ToString());
                    });
                }

                try {
                    return Client.UploadString(Url, Data);
                }
                catch { }
            }
            return null;
        });

        public static Function Server = Function.Create("Server", () => {
            return new Net.Http.Server();
        });

        public static Function Escape = Function.Create("Escape", (string Str) => {
            return System.Uri.EscapeUriString(Str);
        });

        public static Function Descape = Function.Create("Descape", (string Str) => {
            return System.Uri.UnescapeDataString(Str);
        });
    }
}
