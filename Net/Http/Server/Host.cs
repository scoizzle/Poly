using FileSystem = System.IO;

namespace Poly.Net.Http {
    using Data;

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

        public Host() { }

        public Host(string name) {
            Name = name;
        }

        public string GetDocumentName(string Target) {
            if (Target == null || Target.Length == 0)
                return null;

            var length = Target.Length;

            if (Target[length - 1] == '/')
                return string.Concat(Target, DefaultDocument);

            return Target;
        }

        public string GetFullPath(string www) {
            return (DocumentPath?.FullName ?? string.Empty) + www;
        }
    }
}
