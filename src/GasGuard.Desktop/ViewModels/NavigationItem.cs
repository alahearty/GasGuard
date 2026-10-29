namespace GasGuard.Desktop.ViewModels;

public sealed record NavigationItem(string Title, string Code, object Page)
{
    public override string ToString() => Title;
}
