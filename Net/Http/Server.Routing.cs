using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;

namespace Poly.Net.Http {
    using Data;
    using Net.Tcp;
    using Script;

    public partial class Server {
        public Event.Engine Handlers = new Event.Engine();

        public void On(string Path, Event.Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public object Psx(Request Request, string FileName) {
            Cache.Item Cached;

            if (Request.Host.Cache.TryGetValue(FileName, out Cached)) {
                if (Cached.Script == null) {
                    Cached.Script = new Engine();
					Cached.Script.IncludePath = Request.Host.Path + Path.DirectorySeparatorChar;

                    Cached.Script.ReferencedTypes.Add("Response", typeof(Result));

                    if (Cached.Script.Parse(Encoding.UTF8.GetString(Cached.Content))) {
                        return Cached.Script.Evaluate(Request);
                    }
                    else {
                        Cached.Script = null;
                    }
                }
                else {
                    return Cached.Script.Evaluate(Request);
                }

            }

            return null;
        }
    }
}
