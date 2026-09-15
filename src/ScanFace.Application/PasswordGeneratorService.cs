using System.Security.Cryptography;

namespace ScanFace.Application;

public sealed class PasswordGeneratorService
{
    private const string Lowercase = "abcdefghijkmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";

    public string Generate(int length = 20, bool includeSymbols = true)
    {
        if (length is < 12 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "O tamanho deve ficar entre 12 e 128 caracteres.");
        }

        var groups = new List<string> { Lowercase, Uppercase, Digits };
        if (includeSymbols)
        {
            groups.Add(Symbols);
        }

        var result = new char[length];
        var index = 0;
        foreach (var group in groups)
        {
            result[index++] = group[RandomNumberGenerator.GetInt32(group.Length)];
        }

        var alphabet = string.Concat(groups);
        while (index < result.Length)
        {
            result[index++] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        for (var i = result.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return new string(result);
    }
}
