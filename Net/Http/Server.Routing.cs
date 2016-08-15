using System;
using System.Text;
using System.IO;

namespace Poly.Net.Http {
    using Script;

    public partial class Server {
        public object Psx(Request Request, string FileName) {
            Cache.Item CacheItem;
            object Result = null;

            if (Request.Host.Cache.TryGetValue(FileName, out CacheItem)) {
                var Cached = CacheItem as Cache.PolyScriptItem;

                if (Cached == null)
                    return null;

                if (Cached.Script == null) {
                    Cached.Script = new Engine();
                    Cached.Script.IncludePath = Request.Host.Path + Path.DirectorySeparatorChar;

                    Cached.Script.Static.Set("Server", this);
                    Cached.Script.ReferencedTypes.Add("Response", typeof(Result));
                    
                    if (!Cached.Script.Parse(Cached.Text))
                        Cached.Script = null;
                }

                Result = Cached.Script?.Evaluate(Request);
            }

            return Result;
        }
    }
}
