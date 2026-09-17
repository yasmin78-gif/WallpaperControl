using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl
{
    internal static class WindowsSecretProtector
    {
        private const int CryptProtectUiForbidden = 0x1;

        /// <summary>
        /// Encrypts nonempty UTF-8 text with Windows DPAPI and stores the result as Base64.
        /// </summary>
        /// <param name="value">The plaintext secret to encrypt for the current Windows user.</param>
        /// <returns>The Base64 DPAPI payload, or an empty string for empty input.</returns>
        public static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(ProtectBytes(bytes));
        }

        /// <summary>
        /// Decrypts a saved DPAPI value, returning an empty string for missing or unreadable data.
        /// </summary>
        /// <param name="protectedValue">The saved Base64-encoded DPAPI payload.</param>
        /// <returns>The decrypted text, or an empty string when input is missing or cannot be decrypted.</returns>
        public static string Unprotect(string protectedValue)
        {
            if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
            try
            {
                byte[] bytes = Convert.FromBase64String(protectedValue);
                return Encoding.UTF8.GetString(UnprotectBytes(bytes));
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Encrypts bytes with the current Windows user&apos;s DPAPI context and releases temporary native buffers.
        /// </summary>
        /// <param name="bytes">The binary data to process.</param>
        /// <returns>The encrypted DPAPI payload in a managed byte array.</returns>
        private static byte[] ProtectBytes(byte[] bytes)
        {
            DataBlob input = CreateBlob(bytes);
            DataBlob output = default;
            try
            {
                if (!CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
                    throw new InvalidOperationException("Windows DPAPI encryption failed.");
                return CopyBlob(output);
            }
            finally
            {
                // Match each buffer to its allocator: input uses AllocHGlobal,
                // whereas DPAPI allocates output that must be released with LocalFree.
                if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }

        /// <summary>
        /// Decrypts DPAPI bytes and releases temporary native buffers.
        /// </summary>
        /// <param name="bytes">The binary data to process.</param>
        /// <returns>The decrypted bytes in a managed array.</returns>
        private static byte[] UnprotectBytes(byte[] bytes)
        {
            DataBlob input = CreateBlob(bytes);
            DataBlob output = default;
            try
            {
                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, ref output))
                    throw new InvalidOperationException("Windows DPAPI decryption failed.");
                return CopyBlob(output);
            }
            finally
            {
                // The managed interop allocation and the DPAPI allocation require
                // different release functions even though both are native pointers.
                if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }

        /// <summary>
        /// Copies managed bytes into a native buffer owned by the caller.
        /// </summary>
        /// <param name="bytes">The binary data to process.</param>
        /// <returns>A native copy of the bytes whose buffer must be released with FreeHGlobal.</returns>
        private static DataBlob CreateBlob(byte[] bytes)
        {
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return new DataBlob { cbData = bytes.Length, pbData = ptr };
        }

        /// <summary>
        /// Copies a native data blob into a new managed byte array without taking ownership of the blob.
        /// </summary>
        /// <param name="blob">The native data blob to copy; ownership remains with the caller.</param>
        /// <returns>A managed copy of the native buffer.</returns>
        private static byte[] CopyBlob(DataBlob blob)
        {
            if (blob.cbData <= 0 || blob.pbData == IntPtr.Zero) return Array.Empty<byte>();
            byte[] result = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, result, 0, blob.cbData);
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int cbData;
            public IntPtr pbData;
        }

        /// <summary>
        /// Encrypts a native data blob using Windows DPAPI.
        /// </summary>
        /// <param name="pDataIn">The native buffer containing the plaintext or encrypted input.</param>
        /// <param name="szDataDescr">An optional description stored with the protected data.</param>
        /// <param name="pOptionalEntropy">Optional additional DPAPI entropy, or zero when none is used.</param>
        /// <param name="pvReserved">Reserved by Windows; pass zero.</param>
        /// <param name="pPromptStruct">Optional DPAPI prompt options, or zero when no prompt is used.</param>
        /// <param name="dwFlags">The option flags defined by the invoked native API.</param>
        /// <param name="pDataOut">Receives the DPAPI output buffer, which the caller must release with LocalFree.</param>
        /// <returns>True when encryption succeeds; otherwise, false.</returns>
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        /// <summary>
        /// Decrypts a native data blob using Windows DPAPI.
        /// </summary>
        /// <param name="pDataIn">The native buffer containing the plaintext or encrypted input.</param>
        /// <param name="ppszDataDescr">An optional pointer receiving the stored description, or zero when it is not requested.</param>
        /// <param name="pOptionalEntropy">Optional additional DPAPI entropy, or zero when none is used.</param>
        /// <param name="pvReserved">Reserved by Windows; pass zero.</param>
        /// <param name="pPromptStruct">Optional DPAPI prompt options, or zero when no prompt is used.</param>
        /// <param name="dwFlags">The option flags defined by the invoked native API.</param>
        /// <param name="pDataOut">Receives the DPAPI output buffer, which the caller must release with LocalFree.</param>
        /// <returns>True when decryption succeeds; otherwise, false.</returns>
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        /// <summary>
        /// Releases a buffer allocated by a Windows API that uses the local allocator.
        /// </summary>
        /// <param name="hMem">The locally allocated native buffer to release.</param>
        /// <returns>Zero on success, or the original handle when the release fails.</returns>
        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
