using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Rayler7.Infrastructure.Security;

/// <summary>
/// Hash de senha com Argon2id, salt individual de 16 bytes e parâmetros gravados junto ao hash
/// (formato: argon2id$v=19$m=65536,t=3,p=2$salt$hash), para poder aumentá-los no futuro.
/// </summary>
public sealed class PasswordHasher
{
    public int MemoryKib { get; init; } = 65536;
    public int Iterations { get; init; } = 3;
    public int Parallelism { get; init; } = 2;
    public const int SaltBytes = 16;
    public const int HashBytes = 32;
    public const int MinimumLength = 8;

    public string Hash(string password)
    {
        ValidatePolicy(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, MemoryKib, Iterations, Parallelism);
        return string.Create(CultureInfo.InvariantCulture,
            $"argon2id$v=19$m={MemoryKib},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public bool Verify(string password, string encoded)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        var parts = encoded.Split('$');
        if (parts.Length != 5 || parts[0] != "argon2id" || parts[1] != "v=19")
        {
            return false;
        }

        var p = parts[2].Split(',').Select(kv => kv.Split('=')).ToDictionary(kv => kv[0], kv => int.Parse(kv[1], CultureInfo.InvariantCulture));
        var salt = Convert.FromBase64String(parts[3]);
        var expected = Convert.FromBase64String(parts[4]);
        var actual = Compute(password, salt, p["m"], p["t"], p["p"]);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string encoded) =>
        !encoded.Contains(string.Create(CultureInfo.InvariantCulture, $"m={MemoryKib},t={Iterations},p={Parallelism}"), StringComparison.Ordinal);

    public static void ValidatePolicy(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length < MinimumLength)
        {
            throw new ArgumentException($"A senha deve ter pelo menos {MinimumLength} caracteres.", nameof(password));
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            throw new ArgumentException("A senha deve conter letras e números.", nameof(password));
        }
    }

    private static byte[] Compute(string password, byte[] salt, int memory, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memory,
            Iterations = iterations,
            DegreeOfParallelism = parallelism,
        };
        return argon.GetBytes(HashBytes);
    }
}
