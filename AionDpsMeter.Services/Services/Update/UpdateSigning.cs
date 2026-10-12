using System.Security.Cryptography;
using System.Text;

namespace AionDpsMeter.Services.Services.Update
{
    /// <summary>
    /// Release signature checks. Shared (as a linked source file) by the app, the Updater and
    /// tools/ReleaseSigner so all three build the signed message the same way.
    /// The private key never lives in this repository.
    /// </summary>
    public static class UpdateSigning
    {
        /// <summary>ECDSA P-256 public key, SubjectPublicKeyInfo DER, base64.</summary>
        public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAERnza3JawX5rIb7h3gsFPkTfCdAl2stwGSTcK5ucrb1FF5ca8EMG9V/5mD+0ROj8w16s0oNRRzflvS/i7CF7kzA==";

        public const string SignatureExtension = ".sig";

        /// <summary>
        /// The tag is part of the signed message so a genuine older release can't be
        /// re-labelled as a newer version.
        /// </summary>
        public static byte[] BuildMessage(string tag, byte[] zipSha256) =>
            Encoding.UTF8.GetBytes($"AionDpsMeter-update\n{tag}\n{Convert.ToHexString(zipSha256)}");

        public static byte[] HashFile(string path)
        {
            using var stream = File.OpenRead(path);
            return SHA256.HashData(stream);
        }

        public static bool Verify(string zipPath, string tag, byte[] signature)
        {
            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
                return ecdsa.VerifyData(BuildMessage(tag, HashFile(zipPath)), signature, HashAlgorithmName.SHA256);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                return false;
            }
        }
    }
}
