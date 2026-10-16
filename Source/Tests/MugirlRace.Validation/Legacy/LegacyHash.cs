using System.Security.Cryptography;
using System.Text;

namespace MugirlRace.Validation.Legacy;

/// <summary>语料输入的指纹：文件 sha256（大写十六进制）与 Git blob id（与 <c>git hash-object</c> 相同）。</summary>
internal static class LegacyHash
{
    public static string Sha256File(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static string Sha256(string text) => Sha256(Encoding.UTF8.GetBytes(text));

    /// <summary><c>sha1("blob &lt;length&gt;\0" + bytes)</c>，小写。</summary>
    public static string GitBlob(byte[] bytes)
    {
        byte[] header = Encoding.ASCII.GetBytes($"blob {bytes.Length}\0");
        byte[] all = new byte[header.Length + bytes.Length];
        header.CopyTo(all, 0);
        bytes.CopyTo(all, header.Length);
#pragma warning disable CA5350 // Git 的对象 id 就是 SHA-1；这里只用于对照 Git 历史，不作安全用途。
        return Convert.ToHexString(SHA1.HashData(all)).ToLowerInvariant();
#pragma warning restore CA5350
    }
}
