using System.Collections.Generic;
using System.Linq;

namespace Poly.Net.Http {
    using Collections;

    public class RangeHeader : Header {
        public long StartIndex;
        public long LastIndex;

        public RangeHeader() : base("Range") { }
        
        public override IEnumerable<string> Serialize() =>
            StartIndex != 0 && LastIndex != 0 ?
                new [] { $"bytes={StartIndex}-{LastIndex}" }:
                Enumerable.Empty<string>();                

        public override void Deserialize(StringIterator value) =>
            TryDeserialize(value);

        public override void Reset() =>
            StartIndex = LastIndex = 0;

        private bool TryDeserialize(StringIterator it) {
            if (!it.ConsumeIgnoreCase("bytes="))
                return false;
            
            if (!it.Extract(out long start_index))
                return false;
            
            if (!it.Consume('-'))
                return false;
                
            if (!it.Extract(out long last_index))
                return false;                

            StartIndex = start_index;
            LastIndex = last_index;
            return true;
        }
    }
}