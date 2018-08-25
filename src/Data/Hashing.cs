using System.Security.Cryptography;

namespace Poly.Data {

    public static class Hashing {
        private static HashAlgorithm md5 = MD5.Create();
        private static HashAlgorithm sha256 = SHA256.Create();
        private static HashAlgorithm sha512 = SHA512.Create();
        private static HashAlgorithm sha1 = SHA1.Create();

        private static byte[] getHash(HashAlgorithm alg, byte[] toHash) {
            try {
                return alg.ComputeHash(toHash);
            }
            catch {
                return null;
            }
        }

        public static byte[] GetMD5(byte[] toHash) {
            return getHash(md5, toHash);
        }

        public static byte[] GetSHA1(byte[] toHash) {
            return getHash(sha1, toHash);
        }

        public static byte[] GetSHA256(byte[] toHash) {
            return getHash(sha256, toHash);
        }

        public static byte[] GetSHA512(byte[] toHash) {
            return getHash(sha512, toHash);
        }
    }
}