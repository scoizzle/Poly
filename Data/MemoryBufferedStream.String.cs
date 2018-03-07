using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Data {

    public partial class MemoryBufferedStream {
        public bool Write(string text, Encoding encoding) =>
            Out.Read(encoding.GetBytes(text));

        public string ReadStringUntilConstrained(byte[] chain, Encoding encoding) =>
            ReadUntilConstrained(chain, _ => encoding.GetString(_.Array, _.Offset, _.Count));

        public Task<string> ReadStringUntilConstrainedAsync(byte[] chain, Encoding encoding, CancellationToken cancellation_token) =>
            ReadUntilConstrainedAsync(chain, _ => encoding.GetString(_.Array, _.Offset, _.Count), cancellation_token);
    }
}
