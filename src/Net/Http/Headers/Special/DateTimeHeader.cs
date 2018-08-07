using System;
using System.Globalization;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace Poly.Net.Http {
    using Data;

    public class DateTimeHeader : Header {        
        public DateTimeHeader(string key) : base(key) { }

        new public DateTime? Value { get; set; }

        public override IEnumerable<string> Serialize() =>
            Value.HasValue ?
                new [] { Value?.ToString("r") } :
                Enumerable.Empty<string>();

        public override void Deserialize(string value) =>
            Value = 
                DateTime.TryParseExact(value, "r", CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime result) ?
                    result :
                    default;

        public override void Reset() =>
            Value = default;
    }
}