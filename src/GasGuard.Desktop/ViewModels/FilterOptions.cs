namespace GasGuard.Desktop.ViewModels;

public sealed record ChoiceOption<T>(string Label, T Value)
{
    public override string ToString() => Label;
}
