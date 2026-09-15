namespace ScanFace.Application;

public sealed record PasswordGeneratorOptions
{
    public int Length { get; init; } = 20;
    public bool IncludeUppercase { get; init; } = true;
    public bool IncludeLowercase { get; init; } = true;
    public bool IncludeNumbers { get; init; } = true;
    public bool IncludeSymbols { get; init; } = true;
    public int MinimumNumbers { get; init; } = 1;
    public int MinimumSymbols { get; init; } = 1;
    public bool AvoidAmbiguousCharacters { get; init; } = true;
}
