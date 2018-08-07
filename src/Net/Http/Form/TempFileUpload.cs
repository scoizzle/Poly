using System.IO;

namespace Poly.Net.Http {

    public class TempFileUpload {
        public FileInfo LocalInfo;

        public string Name;
        public string ContentType;

        public long Size {
            get { return LocalInfo.Length; }
        }
    }
}