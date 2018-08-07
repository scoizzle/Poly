using System;
using Xunit;

namespace Poly.UnitTests {

    public class BitArray {
        [Fact]
        public void ReadOnly() {
            var array = "Test string".GetBytes();
            var bit_array = new Data.BitArray(array, 0, array.Length);
            
            var first = bit_array[0];
            bit_array[0] = !first;
            
            Assert.Equal(first, bit_array[0]);
        }

        [Fact]
        public void Write() {
            var bit_array = new Data.BitArray();
            var chr = 'a';
            
            for (var i = 0; i < 8; i++) {
                bit_array.Write((chr & (0x80 >> i)) != 0);
            }

            Assert.True(bit_array.BitPosition == 8, "Should be at the 9th (8) bit position after writing 8 bits.");
            Assert.True(bit_array.LastIndex == 1, "LastIndex should be equal to Bytes.Length.");
            Assert.True(bit_array.Bytes[0] == (int)(chr), "The first byte should be equal to the single byte character written to the bit array.");
        }
    }
}