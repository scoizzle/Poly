using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;

namespace Poly.Data {
    public class jsFile<T> : jsObject<T> where T: class {
        private DateTime LastWriteTime;
        public string FileName = string.Empty;

        public bool IsCurrent {
            get {
                return File.GetLastWriteTime(FileName) == LastWriteTime;
            }
        }

        public bool Load(string FileName) {
            if (!File.Exists(FileName)) {
                return false;
            }
            this.FileName = FileName;
            LastWriteTime = File.GetLastWriteTime(FileName);
            return Parse(File.ReadAllText(FileName));
        }

        public bool Save(string FileName, bool HumanFormatting = false) {
            this.FileName = FileName;
            File.WriteAllText(FileName, this.ToString(HumanFormatting));
            LastWriteTime = File.GetLastWriteTime(FileName);
            return true;
        }
    }

    public class jsFile : jsFile<object> {
    }
}
