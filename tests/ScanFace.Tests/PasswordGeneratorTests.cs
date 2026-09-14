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
}
