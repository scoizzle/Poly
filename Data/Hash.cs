using System;
using System.Security.Cryptography;
using System.Text;

namespace Poly.Data {
    public class Hash {
        private static HashAlgorithm md5 = HashAlgorithm.Create("MD5");
        private static HashAlgorithm sha256 = HashAlgorithm.Create("SHA256");
        private static HashAlgorithm sha512 = HashAlgorithm.Create("SHA512");
        private static HashAlgorithm sha1 = HashAlgorithm.Create("SHA1");
        private static string HexAlph = "0123456789ABCDEF";

		private static string getHash(HashAlgorithm alg, byte[] toHash) {
            byte[] btHash = null;

            try {
                btHash = alg.ComputeHash(toHash);
            }
            catch {
                return null;
            }

            char[] szHash = new char[btHash.Length * 2];

            for (int x = 0, y = 0; x < btHash.Length && y < szHash.Length; x++, y += 2) {
                szHash[y] = HexAlph[(btHash[x] >> 4)];
                szHash[y + 1] = HexAlph[(btHash[x] & 0xF)];
            }

            btHash = null;

            return new string(szHash);
		}
		
		public static string MD5(byte[] toHash) {
            return getHash(md5, toHash);
		}

        public static string SHA1(byte[] toHash) {
            return getHash(sha1, toHash);
        }

        public static string SHA256(byte[] toHash) {
            return getHash(sha256, toHash);
		}

        public static string SHA512(byte[] toHash) {
            return getHash(sha512, toHash);
		}
	}
}

