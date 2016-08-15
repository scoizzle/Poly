using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Poly {
    static class ByteArray {
        public static string ConvertToString(this byte[] This) {
            return Encoding.Default.GetString(This);
        }

        public static int FindSubByteArray(this byte[] This, byte[] sub) {
            return FindSubByteArray(This, 0, sub, 0, sub.Length);
        }

        public static int FindSubByteArray(this byte[] This, int index, byte[] sub) {
            return FindSubByteArray(This, index, sub, 0, sub.Length);
        }

        public static int FindSubByteArray(this byte[] This, int index, byte[] sub, int subindex, int sublength) {
            if (index < 0 || index > This.Length || sub == null || subindex < 0 || subindex + sublength > sub.Length)
                return -1;

            for (; index < This.Length; index++) {
                if (This[index] == sub[subindex]) {
                    bool found = true;

                    for (var si = 1; si < sublength; si++) {
                        if (This[index + si] != sub[subindex + si]) {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                        return index;
                }
            }

            return -1;
        }

        public static bool CompareSubByteArray(this byte[] This, int index, byte[] sub) {
            return CompareSubByteArray(This, index, sub, 0, sub.Length);
        }

        public static bool CompareSubByteArray(this byte[] This, int index, byte[] sub, int subindex, int sublength) {
            if (index < 0 || index > This.Length || sub == null || subindex < 0 || subindex + sublength > sub.Length)
                return false;
            
            if (This[index] == sub[subindex]) {
                bool found = true;

                for (var si = 1; si < sublength; si++) {
                    if (This[index + si] != sub[subindex + si]) {
                        found = false;
                        break;
                    }
                }

                return found;
            }

            return false;
        }
    }
}
