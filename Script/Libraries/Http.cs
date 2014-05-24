using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Web;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Http : Library {
        public Http() {
            RegisterStaticObject("Http", this);

            Add(Get);
            Add(Post);
            Add(Escape);
            Add(Descape);
            Add(Server);
        }

        public static SystemFunction Get = new SystemFunction("Get", (Args) => {
            var Url = Args.Get<string>("Url");

            if (!string.IsNullOrEmpty(Url)) {
                WebClient Client = new WebClient();

                if (Args.ContainsKey("Headers")) {
                    Args.getObject("Headers").ForEach((Key, Value) => {
                        Client.Headers.Add(Key, Value.ToString());
                    });
                }

                try {
                    return Client.DownloadString(Url);
                }
                catch {  }
            }
            return null;
        }, "Url", "Headers");

        public static SystemFunction Post = new SystemFunction("Post", (Args) => {
            var Url = Args.Get<string>("Url");
            var Data = Args.Get<string>("Data");

            if (!string.IsNullOrEmpty(Url)) {
                WebClient Client = new WebClient();

                if (Args.ContainsKey("Headers")) {
                    Args.getObject("Headers").ForEach((Key, Value) => {
                        Client.Headers.Add(Key, Value.ToString());
                    });
                }

                try {
                    return Client.UploadString(Url, Data);
                }
                catch { }
            }
            return null;
        }, "Url", "Headers", "Data");

        public static SystemFunction Server = new SystemFunction("Server", (Args) => {
            return new Net.Http.Server();
        });

        public static SystemFunction Escape = new SystemFunction("Escape", (Args) => {
            var Str = Args.Get<string>("Str");
            return System.Uri.EscapeUriString(Str);
        }, "Str");

        public static SystemFunction Descape = new SystemFunction("Descape", (Args) => {
            var Str = Args.Get<string>("Str");

            if (string.IsNullOrEmpty(Str))
                return "";

            return System.Uri.UnescapeDataString(Str);
        }, "Str");
    }
}
