using System.IO;

namespace Poly.IO {

    public class TempFileStream : Stream {
        public FileInfo FileInfo { get; private set; }
        public FileStream BaseStream { get; private set; }

        public override bool CanRead { get { return BaseStream.CanRead; } }

        public override bool CanSeek { get { return BaseStream.CanSeek; } }

        public override bool CanWrite { get { return BaseStream.CanRead; } }

        public override long Length { get { return BaseStream.Length; } }

        public override long Position {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
        }

        public TempFileStream() {
            FileInfo = new FileInfo(Path.GetTempFileName());
            BaseStream = FileInfo.Open(FileMode.Create);
        }

        ~TempFileStream() {
            BaseStream.Close();
            FileInfo.Delete();
        }

        public override void Flush() => BaseStream.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            BaseStream.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) =>
            BaseStream.Seek(offset, origin);

        public override void SetLength(long value) =>
            BaseStream.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            BaseStream.Write(buffer, offset, count);
    }
}