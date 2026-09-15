using ScanFace.Application;

namespace ScanFace.Tests;

public sealed class PasswordGeneratorTests
{
    [Fact]
    public void Generate_UsesEveryRequestedCharacterGroup()
    {
        var password = new PasswordGeneratorService().Generate(32, true);

        Assert.Equal(32, password.Length);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, character => !char.IsLetterOrDigit(character));
    }

    [Fact]
    public void Generate_RespectsMinimumNumbersAndSymbols()
    {
        var password = new PasswordGeneratorService().Generate(new PasswordGeneratorOptions
        {
            Length = 40,
            MinimumNumbers = 8,
            MinimumSymbols = 6
        });

        Assert.Equal(40, password.Length);
        Assert.True(password.Count(char.IsDigit) >= 8);
        Assert.True(password.Count(character => !char.IsLetterOrDigit(character)) >= 6);
    }

    [Fact]
    public void Generate_CanExcludeAmbiguousCharacters()
    {
        var password = new PasswordGeneratorService().Generate(new PasswordGeneratorOptions
        {
            Length = 128,
            AvoidAmbiguousCharacters = true
        });

        Assert.DoesNotContain(password, character => "Il1O0o".Contains(character));
    }

    [Fact]
    public void Generate_UsesOnlyEnabledGroups()
    {
        var password = new PasswordGeneratorService().Generate(new PasswordGeneratorOptions
        {
            Length = 24,
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeSymbols = false,
            IncludeNumbers = true,
            MinimumNumbers = 10,
            MinimumSymbols = 0
        });

        Assert.All(password, character => Assert.True(char.IsDigit(character)));
    }

    [Fact]
    public void Generate_RejectsAnEmptyCharacterSet()
    {
        var options = new PasswordGeneratorOptions
        {
            IncludeUppercase = false,
            IncludeLowercase = false,
            IncludeNumbers = false,
            IncludeSymbols = false,
            MinimumNumbers = 0,
            MinimumSymbols = 0
        };

        Assert.Throws<ArgumentException>(() => new PasswordGeneratorService().Generate(options));
    }
}
