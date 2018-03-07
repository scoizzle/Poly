using FileSystem = System.IO;

namespace Poly.Net {

    public partial class HttpServer {

        public class Host {
            public Matcher Matcher;
            public FileSystem.DirectoryInfo DocumentPath;

            public string Name {
                get { return Matcher.Format; }
                set { Matcher = new Matcher(value); }
            }

            public string Path {
                get { return DocumentPath.FullName; }
                set { DocumentPath = new FileSystem.DirectoryInfo(value); }
            }

            public string DefaultDocument = "index.htm";

            public Host() {
            }

            public Host(string name) {
                Name = name;
            }
        }
    }
}