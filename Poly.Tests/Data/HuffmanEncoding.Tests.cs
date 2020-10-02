using System.Linq;

using Xunit;

namespace Poly.Data {
    public class HuffmanEncodingTests {
        static char[] dataset = new [] { 'a', 'a', 'b', 'b', 'b', 'c' };
        static HuffmanEncoding<char> encoding = new HuffmanEncoding<char>(dataset);

        [Fact]
        public void EncodeSingleCharacter()
        {
            var character = dataset[0];
            var encoded = encoding.Encode(character);
            var decoded = encoding.Decode(encoded).Single();

            Assert.Equal(character, decoded);
        }

        [Fact]
        public void EncodeDataSet()
        {
            var encoded = dataset.SelectMany(c => encoding.Encode(c));
            var decoded = encoding.Decode(encoded);
            
            Assert.True(
                Enumerable.SequenceEqual(dataset, decoded)
            );
        }
    }
}