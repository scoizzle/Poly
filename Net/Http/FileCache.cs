using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using Poly;
using Poly.Data;

namespace Poly.Net.Http {
    public class CachedFile {
        public string   Name = "";
        public string   MIME = "text/html";
        public byte[]   Data = new byte[0];
        public DateTime LastWriteTime = default(DateTime);

        public CachedFile(string Name) {
            if (File.Exists(Name)) {
                this.Name = Name;
                this.LastWriteTime = File.GetLastWriteTime(Name).ToUniversalTime();
                this.Data = File.ReadAllBytes(Name);
            }
        }
    }

    public class FileCache : jsObject<CachedFile> {
        public bool IsCurrent(string ObjectName) {
            if (!this.ContainsKey(ObjectName) || !File.Exists(ObjectName))
                return false;

            CachedFile Obj = this[ObjectName];

            if (Obj.LastWriteTime == File.GetLastWriteTime(ObjectName).ToUniversalTime()) {
                return true;
            }
            return false;
        }
    }
}
