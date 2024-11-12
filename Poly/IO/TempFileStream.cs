namespace Poly.IO
{

    public class TempFileStream : Stream
    {
        public string FileName { get; private set; }
        public FileInfo FileInfo { get; private set; }
        public FileStream BaseStream { get; private set; }

        public override bool CanRead => BaseStream.CanRead;

        public override bool CanSeek => BaseStream.CanSeek;

        public override bool CanWrite => BaseStream.CanWrite;

        public override long Length => BaseStream.Length;

        public override long Position
        {
            get { return BaseStream.Position; }
            set { BaseStream.Position = value; }
        }

        public TempFileStream()
        {
            FileName = Path.GetTempFileName();
            FileInfo = new FileInfo(FileName);
            BaseStream = FileInfo.Open(FileMode.Create);
        }

        ~TempFileStream()
        {
            BaseStream.Close();

            if (FileInfo.FullName == FileName)
                FileInfo.Delete();
        }

        public virtual void SaveTo(string file_path) =>
            FileInfo.MoveTo(file_path);

        public override void Flush() =>
            BaseStream.Flush();

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