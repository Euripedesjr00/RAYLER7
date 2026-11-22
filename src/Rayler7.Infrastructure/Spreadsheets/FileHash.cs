using System.Security.Cryptography;

namespace Rayler7.Infrastructure.Spreadsheets;

public static class FileHash
{
    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
