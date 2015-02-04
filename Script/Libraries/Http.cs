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

        public static Function Get = new Function("Get", (Args) => {
            var Url = Args.Get<string>("Url");
            var Headers = Args.getObject("Headers");
            var Uri = default(Uri);

            if (Uri.TryCreate(Url, UriKind.RelativeOrAbsolute, out Uri)) {
                using (var Client = new WebClient()) {
                    if (Headers != null) {
                        Headers.ForEach((K, V) => {
                            Client.Headers.Add(K, V.ToString());
                        });
                    }
                    try {
                        return Client.DownloadString(Uri);
                    }
                    catch { }
                }
            }
            return null;
        }, "Url", "Headers");

        public static Function Post = new Function("Post", (Args) => {
            var Url = Args.Get<string>("Url");

            var Data = Args["Data"] is jsObject ? 
                Args.getObject("Data").ToPostString() : 
                Args.Get<string>("Data");

            var Headers = Args.getObject("Headers");

            if (!string.IsNullOrEmpty(Url)) {
                WebClient Client = new WebClient();

                if (Headers != null) {
                    foreach (var Pair in Headers) {
                        if (Pair.Value == null)
                            continue;

                        Client.Headers.Add(Pair.Key, Pair.Value.ToString());
                    }
                }

                try {
                    return Client.UploadString(Url, Data);
                }
                catch { }
            }
            return null;
        }, "Url", "Headers", "Data");

        public static Function Server = new Function("Server", (Args) => {
            return new Net.Http.Server();
        });

        public static Function Escape = new Function("Escape", (Args) => {
            var Str = Args.Get<string>("Str");
            return System.Uri.EscapeUriString(Str);
        }, "Str");

        public static Function Descape = new Function("Descape", (Args) => {
            var Str = Args.Get<string>("Str");

            if (string.IsNullOrEmpty(Str))
                return "";

            return System.Uri.UnescapeDataString(Str);
        }, "Str");
    }
}
