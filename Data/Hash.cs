using System;
using System.Security.Cryptography;

namespace Poly.Data {
    public class Hash {
        private static HashAlgorithm md5 = HashAlgorithm.Create("MD5");
        private static HashAlgorithm sha256 = HashAlgorithm.Create("SHA256");
        private static HashAlgorithm sha512 = HashAlgorithm.Create("SHA512");

		private static string getHash(HashAlgorithm alg, byte[] toHash) {
			string szHash = string.Empty;
            byte[] btHash = null;

            do {
                try {
                    btHash = alg.ComputeHash(toHash);
                }
                catch { }
            } while (btHash == null);
			
			for (int Index=0;Index<btHash.Length;Index++){
                szHash += btHash[Index].ToString("x2");
			}
			
			return szHash;
		}
		
		public static string MD5(byte[] toHash) {
                return getHash(md5, toHash);
		}

        public static string SHA256(byte[] toHash) {
                return getHash(sha256, toHash);
		}

        public static string SHA512(byte[] toHash) {
                return getHash(sha512, toHash);
		}
	}
}

