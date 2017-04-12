using FileSystem = System.IO;

namespace Poly.Net.Http {
    using Data;

    public class Host {
        internal Matcher Matcher;
        internal FileSystem.DirectoryInfo DocumentPath;

        public string Name {
            get { return Matcher.Format; }
            set { Matcher = new Matcher(value); }
        }

        public string Path {
            get { return DocumentPath.FullName; }
            set { DocumentPath = new FileSystem.DirectoryInfo(value); }
        }

        public string DefaultDocument = "/index.htm", 
                      DefaultExtension = ".htm";

        public Host() { }

        public Host(string name) {
            Name = name;
        }

        public string GetDocumentName(string Target) {
            if (Target == null || Target.Length == 0 || Target == "/")
                Target = DefaultDocument;
            else if (Target[Target.Length - 1] == '/')
                Target = Target + DefaultDocument;
            return Target;
        }

        public string GetFullPath(string www) {
            return DocumentPath?.FullName ?? string.Empty + www;
        }
    }
}
