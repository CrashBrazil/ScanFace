using System.Security.Cryptography;

namespace ScanFace.Application;

public sealed class PasswordGeneratorService
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{};:,.?";
    private static readonly HashSet<char> AmbiguousCharacters = ['I', 'l', '1', 'O', '0', 'o'];

    public string Generate(int length = 20, bool includeSymbols = true) =>
        Generate(new PasswordGeneratorOptions
        {
            Length = length,
            IncludeSymbols = includeSymbols,
            MinimumSymbols = includeSymbols ? 1 : 0
        });

    public string Generate(PasswordGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Length is < 5 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "O tamanho deve ficar entre 5 e 128 caracteres.");
        }

        if (options.MinimumNumbers is < 0 or > 128 || options.MinimumSymbols is < 0 or > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Os mínimos devem ficar entre 0 e 128.");
        }

        if (!options.IncludeNumbers && options.MinimumNumbers > 0)
        {
            throw new ArgumentException("Ative números ou defina o mínimo de números como zero.", nameof(options));
        }

        if (!options.IncludeSymbols && options.MinimumSymbols > 0)
        {
            throw new ArgumentException("Ative caracteres especiais ou defina o mínimo como zero.", nameof(options));
        }

        var groups = new List<(string Characters, int Minimum)>();
        AddGroup(options.IncludeLowercase, Lowercase, 1);
        AddGroup(options.IncludeUppercase, Uppercase, 1);
        AddGroup(options.IncludeNumbers, Digits, Math.Max(1, options.MinimumNumbers));
        AddGroup(options.IncludeSymbols, Symbols, Math.Max(1, options.MinimumSymbols));

        if (groups.Count == 0)
        {
            throw new ArgumentException("Selecione pelo menos um grupo de caracteres.", nameof(options));
        }

        var requiredCharacters = groups.Sum(group => group.Minimum);
        if (requiredCharacters > options.Length)
        {
            throw new ArgumentException("O comprimento é menor que a soma dos mínimos selecionados.", nameof(options));
        }

        var result = new char[options.Length];
        var index = 0;
        foreach (var group in groups)
        {
            for (var count = 0; count < group.Minimum; count++)
            {
                result[index++] = RandomCharacter(group.Characters);
            }
        }

        var alphabet = string.Concat(groups.Select(group => group.Characters));
        while (index < result.Length)
        {
            result[index++] = RandomCharacter(alphabet);
        }

        for (var i = result.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }

        return new string(result);

        void AddGroup(bool include, string characters, int minimum)
        {
            if (!include)
            {
                return;
            }

            var available = options.AvoidAmbiguousCharacters
                ? new string(characters.Where(character => !AmbiguousCharacters.Contains(character)).ToArray())
                : characters;
            groups.Add((available, minimum));
        }
    }

    private static char RandomCharacter(string characters) =>
        characters[RandomNumberGenerator.GetInt32(characters.Length)];
}
