using System.Security.Cryptography;
using System.Text;

namespace Rayler7.Infrastructure.Security;

/// <summary>Protege segredos (tokens, chaves de integração) com a DPAPI do Windows, por usuário.</summary>
public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RAYLER7.v1");

    public static string Protect(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string protectedBase64)
    {
        ArgumentNullException.ThrowIfNull(protectedBase64);
        var data = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }
}
