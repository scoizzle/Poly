using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Poly.Net.Http {
    public class Payload : Stream {
        private Stream Storage;

        public Payload() {
            
        }

        public long ContentLength { get; internal set; }

        public override bool CanRead => true;
        
        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => ContentLength;

        public override long Position { get; set; }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) {
            return 0;
        }

        public override void SetLength(long value) {
        }

        public override void Write(byte[] buffer, int offset, int count) {
        }
    }
}