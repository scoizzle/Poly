using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.IO {

    public partial class MemoryBufferedStream {
        public bool Write(string text, Encoding encoding) =>
            Out.Read(encoding.GetBytes(text));

        public Task<string> ReadStringUntilConstrainedAsync(byte[] chain, Encoding encoding, CancellationToken cancellation_token) =>
            ReadUntilConstrainedAsync(chain, (array, offset, count) => encoding.GetString(array, offset, count), cancellation_token);
    }
}
