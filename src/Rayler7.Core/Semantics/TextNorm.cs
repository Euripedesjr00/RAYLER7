using System.Text;
using Rayler7.Core.Mapping;

namespace Rayler7.Core.Semantics;

/// <summary>Normalização para comparar rótulos: sem acentos, minúsculas, só letras e números separados por um espaço.</summary>
public static class TextNorm
{
    public static string Token(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var folded = InstructionParser.Fold(text);
        var sb = new StringBuilder(folded.Length);
        var space = false;
        foreach (var ch in folded)
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (space && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(ch);
                space = false;
            }
            else
            {
                space = true;
            }
        }

        return sb.ToString();
    }

    public static string[] Words(string? text) => Token(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
