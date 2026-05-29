namespace Dmtd.Module.ViewModels;

public sealed class EnumOption<T> where T : struct, Enum
{
    public required string Label { get; init; }
    public required T Value { get; init; }
}
