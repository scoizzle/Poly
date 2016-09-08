using System;
using System.Text;
using System.IO;

namespace Poly.Net.Http {
    using Script;

    public partial class Server {
        public object Psx(Request Request, string FileName) {
			Cache.Item Cached;

			if (Request.Host.Cache.TryGetValue(FileName, out Cached)) {
                if (Cached.Script == null) {
                    Cached.Script = new Engine();
                    Cached.Script.IncludePath = Request.Host.Path + Path.DirectorySeparatorChar;

                    Cached.Script.Static.Set("Server", this);
                    Cached.Script.ReferencedTypes.Add("Response", typeof(Result));

					if (!Cached.Script.Parse(Cached.Content.GetString()))
                        return Cached.Script = null;
                }
                
                var Result = Cached.Script.Evaluate(Request);

                Request.Result.Content = new MemoryStream(
                    Encoding.Default.GetBytes(Request.OutputBuilder.ToString())
                );

				return Result;
            }

            return null;
        }
    }
}
