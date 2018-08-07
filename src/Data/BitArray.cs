using System;
using System.Collections;
using System.Collections.Generic;

namespace Poly.Data {
    public class BitArray {
        public readonly bool ReadOnly;
        public readonly int Index;

        public byte[] Bytes;
        public int BitPosition;
        public int LastIndex;

        public BitArray() {
            Bytes = new byte[1];
            LastIndex = 1;
        }
        
        public BitArray(byte[] bytes) 
            : this(bytes, 0, bytes.Length) { }

        public BitArray(byte[] bytes, int index, int count) {
            ReadOnly = true;
            BitPosition = 0;

            Bytes = bytes;
            Index = index;
            LastIndex = index + count;
        }

        public bool this[int position] {
            get => Get(position);
            set => Set(position, value);
        }

        public IEnumerable<bool> GetAll() {
            var bytes = Bytes;
            var start = Index;
            var stop = LastIndex;

            while (start < stop) {
                var b = bytes[start++];

                yield return (b & 0b10000000) != 0;
                yield return (b & 0b01000000) != 0;
                yield return (b & 0b00100000) != 0;
                yield return (b & 0b00010000) != 0;
                yield return (b & 0b00001000) != 0;
                yield return (b & 0b00000100) != 0;
                yield return (b & 0b00000010) != 0;
                yield return (b & 0b00000001) != 0;
            }
        }

        public bool Get() => Get(BitPosition);
        public void Set(bool value) => Set(BitPosition, value);

        public bool Get(int position) {
            var major = position / 8;

            if (!BoundsCheck(major))
                throw new ArgumentException();

            var minor = 7 - (position % 8);
            var mask  = 1 << minor;

            return (Bytes[major] & mask) != 0;
        }

        public void Set(int position, bool value) {
            if (ReadOnly)
                return;

            var major = position / 8;

            if (!BoundsCheck(major))
                Resize(major + 1);

            var minor = 7 - (position % 8);
            var mask  = 1 << minor;
            var old   = Bytes[major];

            if (value) {
                Bytes[major] = (byte)(old | mask);
            }
            else {
                Bytes[major] = (byte)(old & ~mask);
            }
        }

        public bool Read() => 
            Get(BitPosition ++);

        public void Write(bool value) =>
            Set(BitPosition ++, value);

        public void End(bool value) {
            var pos = BitPosition;
            var lst = LastIndex * 8;

            while (pos < lst)
                Set(pos ++, value);

            BitPosition = pos;
        }

        private bool BoundsCheck(int byteIndex) =>
            byteIndex >= Index && byteIndex < LastIndex;

        private void Resize(int sizeInBytes) {
            Array.Resize(ref Bytes, sizeInBytes);
            LastIndex = sizeInBytes;
        }
    }
}