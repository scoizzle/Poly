using System.Linq;
using path = System.IO.Path;

namespace Poly.Net.Http {
	using Data;

    public class Host {
        public Matcher Matcher;
        public string Path, DefaultDocument, DefaultExtension;
        public string[] Compressable;

        public string Name {
            get { return Matcher.Format; }
            set { Matcher = new Matcher(value); }
        }

        public Event.Engine<Request.Handler> Handlers;
        public Cache Cache;

		public Host(string hostName) {
			Name = hostName;
			Path = "WWW";
			DefaultDocument = "/index.htm";
			DefaultExtension = "htm";
			Handlers = new Event.Engine<Request.Handler>();
			Compressable = new string[0];
		}
        public void Ready() {
            Path = path.GetFullPath(Path);
            Cache = new Cache(Path, Compressable);
        }

        public void On(string Path, Request.Handler Handler) {
            Handlers.Register(Path, Handler);
        }

        public string GetFullPath(string Target) {
            if (Target == null || Target.Length == 0 || Target == "/")
                Target = DefaultDocument;
            else if (Target[Target.Length - 1] == '/')
                Target = Target + DefaultDocument;
            return Target;
        }

        public override string ToString() {
            return Name;
		}
    }
}
