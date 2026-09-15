using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WallpaperControl
{
    internal static class WindowsSecretProtector
    {
        private const int CryptProtectUiForbidden = 0x1;

        public static string Protect(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(ProtectBytes(bytes));
        }

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
                if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }

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
                if (input.pbData != IntPtr.Zero) Marshal.FreeHGlobal(input.pbData);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }

        private static DataBlob CreateBlob(byte[] bytes)
        {
            IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            return new DataBlob { cbData = bytes.Length, pbData = ptr };
        }

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

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DataBlob pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DataBlob pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
