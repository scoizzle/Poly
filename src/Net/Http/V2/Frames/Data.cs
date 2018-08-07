using System;

namespace Poly.Net.Http.V2.Frames {
    public class Data : Frame {
        public uint Length => Header.Length;

        public byte PaddingLength { get; set; }
        public byte[] Content { get; set; }

        
    }
}