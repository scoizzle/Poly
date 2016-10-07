using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Poly.Net.Http {
    using Data;

    public class TempFileUpload : jsComplex {
        public string FileName;
        public FileInfo Info;

        public TempFileUpload(string FileName) {
            this.FileName = FileName;
            Info = new FileInfo(Path.GetTempFileName());
        }

        ~TempFileUpload() {
            Info.Delete();
        }

        public FileStream GetWriteStream() {
            return Info.OpenWrite();
        }

        public FileStream GetReadStream() {
            return Info.OpenRead();
        }

        public TextReader GetTextStream() {
            return Info.OpenText();
        }

        public void SaveFile(string Location) {
            Info.MoveTo(Location);
        }

        public override bool TryGet(string Key, out object Value) {
            switch (Key) {
                default:
                return base.TryGet(Key, out Value);

                case "FileName": Value = FileName; break;
                case "Info": Value = Info; break;
            }
            return true;
        }

        public override void AssignValue(string Key, object Value) {
            switch (Key) {
                default: base.AssignValue(Key, Value); break;

                case "FileName": FileName = Value?.ToString(); break;
            }
        }
    }
}
