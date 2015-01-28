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
        /* func Get(Url, Headers) {
         *   Client = System.Web.WebClient();
         *   
         *   foreach (Header in Headers)
         *     Client.Headers.Add(Header.Key, Header.Value.ToString());
         *     
         *   try 
         *     return Client.DownloadString(Url);
         *   
         *   return null;
         * }
         */

        public static Function _Get = new Function("Get", (Event.Handler)null, "Url", "Headers") {
            Elements = new Node[] {
                new Expressions.Assign( 
                    new Variable(new Engine(), "Client"),
                    new Expressions.Call(
                        new Engine(), 
                        new Helpers.SystemTypeGetter("System.Web.WebClient"), 
                        "WebClient"
                    )
                ),
                new Expressions.Foreach() { 
                    Variable = new Variable(new Engine(), "Header"),
                    List = new Variable(new Engine(), "Headers"),
                    Elements = new Node[] {
                        new Expressions.Call(
                            new Engine(), 
                            new Variable(new Engine(), "Client.Headers"), 
                            "Add", 
                            new Node[] { 
                                new Variable(new Engine(), "Header.Key"), 
                                new Variable(new Engine(), "Header.Value")
                            }
                        )
                    }
                },
                new Expressions.Try() {
                    Node = new Node() { Elements = new Node[] {
                        new Expressions.Return(){
                            Value = new Expressions.Call(
                                new Engine(), 
                                new Variable(new Engine(), "Client"), 
                                "DownloadString", 
                                new Node[] { 
                                    new Variable(new Engine(), "Url")
                                }
                            )
                        }
                    }}
                },
                new Expressions.Return() {
                    Value = Expression.Null
                }
            }
        };

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
