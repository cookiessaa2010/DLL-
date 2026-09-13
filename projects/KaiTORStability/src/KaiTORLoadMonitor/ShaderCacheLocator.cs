using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace KaiTORLoadMonitor
{
    internal sealed class ShaderCacheInfo
    {
        public string Path { get; set; }
        public string Mode { get; set; }
        public bool IsKaiRedirect { get; set; }
        public bool IsVerifiedNative { get; set; }

        public string Describe()
        {
            var state = Directory.Exists(Path) ? "папка найдена" : "папка будет создана при необходимости";
            return Mode + ": " + Path + " (" + state + ")";
        }
    }

    internal static class ShaderCacheLocator
    {
        private const string OriginalNativeSha256 = "9589f5b59c9649461817ac04620e942303590ce93d0b643d17cababd8f581bd3";

        private const int OffCallCommonAppData = 0x0C557B;
        private const int OffInitialAppend = 0x0C5580;
        private const int OffPathLength = 0x0C55A3;
        private const int OffLiteralLoad = 0x0C55B7;
        private const int OffLiteralCopyRest = 0x0C55C1;
        private const int OffShaderSuffix = 0x0C55E5;
        private const int OffPathLiteral = 0x00ADD6A8;

        private static readonly byte[] LiteralLoadSignature =
        {
            0x0F, 0x10, 0x05, 0xEA, 0x82, 0xA1, 0x00
        };

        public static ShaderCacheInfo Inspect(string gameRoot)
        {
            var standardPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Mount and Blade II Bannerlord",
                "Shaders");

            var result = new ShaderCacheInfo
            {
                Path = standardPath,
                Mode = "Стандартный shader cache",
                IsKaiRedirect = false,
                IsVerifiedNative = false
            };

            try
            {
                var nativePath = Path.Combine(
                    gameRoot,
                    "bin",
                    "Win64_Shipping_Client",
                    "TaleWorlds.Native.dll");

                if (!File.Exists(nativePath))
                {
                    result.Mode = "Shader cache (TaleWorlds.Native.dll не найден для проверки)";
                    return result;
                }

                var data = File.ReadAllBytes(nativePath);
                string redirectedPath;
                if (TryReadKaiRedirect(data, out redirectedPath))
                {
                    result.Path = redirectedPath.TrimEnd('\\');
                    result.Mode = "Kai Shader Cache Redirector";
                    result.IsKaiRedirect = true;
                    result.IsVerifiedNative = true;
                    return result;
                }

                var hash = ComputeSha256(data);
                if (string.Equals(hash, OriginalNativeSha256, StringComparison.OrdinalIgnoreCase))
                {
                    result.IsVerifiedNative = true;
                    return result;
                }

                result.Mode = "Стандартный путь (native DLL отличается от известной оригинальной/наш патч не обнаружен)";
                return result;
            }
            catch (Exception ex)
            {
                result.Mode = "Shader cache: автоопределение не удалось — " + ex.GetType().Name;
                return result;
            }
        }

        internal static bool SelfTest()
        {
            const string target = "D:\\MNB\\Shaders\\";
            var targetBytes = Encoding.ASCII.GetBytes(target);
            var data = new byte[OffPathLiteral + 16];

            Fill(data, OffCallCommonAppData, 5, 0x90);
            Fill(data, OffInitialAppend, 35, 0x90);
            data[OffPathLength] = 0x83;
            data[OffPathLength + 1] = 0xC3;
            data[OffPathLength + 2] = (byte)targetBytes.Length;
            Array.Copy(LiteralLoadSignature, 0, data, OffLiteralLoad, LiteralLoadSignature.Length);
            Fill(data, OffLiteralCopyRest, 33, 0x90);
            Fill(data, OffShaderSuffix, 46, 0x90);
            Array.Copy(targetBytes, 0, data, OffPathLiteral, targetBytes.Length);
            data[OffPathLiteral + targetBytes.Length] = 0;

            string detected;
            return TryReadKaiRedirect(data, out detected) &&
                   string.Equals(detected, target, StringComparison.Ordinal);
        }

        private static bool TryReadKaiRedirect(byte[] data, out string target)
        {
            target = null;
            if (data == null || data.Length <= OffPathLiteral + 15) return false;
            if (!IsNopRange(data, OffCallCommonAppData, 5)) return false;
            if (!IsNopRange(data, OffInitialAppend, 35)) return false;
            if (data[OffPathLength] != 0x83 || data[OffPathLength + 1] != 0xC3) return false;
            if (!BytesEqual(data, OffLiteralLoad, LiteralLoadSignature)) return false;
            if (!IsNopRange(data, OffLiteralCopyRest, 33)) return false;
            if (!IsNopRange(data, OffShaderSuffix, 46)) return false;

            var length = (int)data[OffPathLength + 2];
            if (length < 1 || length > 15) return false;
            if (OffPathLiteral + length >= data.Length) return false;
            if (data[OffPathLiteral + length] != 0) return false;

            var pathBytes = new byte[length];
            Array.Copy(data, OffPathLiteral, pathBytes, 0, length);
            for (var i = 0; i < pathBytes.Length; i++)
            {
                if (pathBytes[i] < 0x20 || pathBytes[i] > 0x7E) return false;
            }

            var value = Encoding.ASCII.GetString(pathBytes);
            if (value.Length < 3 || value[1] != ':' || value[2] != '\\') return false;
            target = value;
            return true;
        }

        private static string ComputeSha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(data);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }

        private static bool IsNopRange(byte[] data, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length) return false;
            for (var i = 0; i < length; i++)
            {
                if (data[offset + i] != 0x90) return false;
            }
            return true;
        }

        private static bool BytesEqual(byte[] data, int offset, byte[] expected)
        {
            if (offset < 0 || expected == null || offset + expected.Length > data.Length) return false;
            for (var i = 0; i < expected.Length; i++)
            {
                if (data[offset + i] != expected[i]) return false;
            }
            return true;
        }

        private static void Fill(byte[] data, int offset, int length, byte value)
        {
            for (var i = 0; i < length; i++) data[offset + i] = value;
        }
    }
}
