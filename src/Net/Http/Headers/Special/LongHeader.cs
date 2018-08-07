using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Poly.Net.Http {
    using Data;

    public class LongHeader : Header {     
        public LongHeader(string key) : base(key) { }

        new public long Value { get; set; }

        public override IEnumerable<string> Serialize() =>
            new [] { Value.ToString() };

        public override void Deserialize(string value) =>
            Value = value.TryParse(out long result) ?
                result : default;

        public override void Reset() =>
            Value = default;
    }
}