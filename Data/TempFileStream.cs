using System;
using System.IO;

namespace Poly.Data {
    public class TempFileStream : Stream {
        public FileInfo Info { get; private set; }
        public FileStream Base { get; private set; }

        public override bool CanRead { get { return Base.CanRead; } }

        public override bool CanSeek { get { return Base.CanSeek; } }

        public override bool CanWrite { get { return Base.CanRead; } }

        public override long Length { get { return Base.Length; } }

        public override long Position { 
            get { return Base.Position; } 
            set { Base.Position = value; }
        }

        public TempFileStream() {
            Info = new FileInfo(Path.GetTempFileName());
            Base = Info.Open(FileMode.Create);
        }

        ~TempFileStream() {
            Base.Close();
            Info.Delete();
        }

        public override void Flush() => Base.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            Base.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            Base.Seek(offset, origin);

        public override void SetLength(long value) =>
            Base.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            Base.Write(buffer, offset, count);
    }
}
