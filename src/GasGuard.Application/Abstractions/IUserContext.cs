namespace GasGuard.Application.Abstractions;

public interface IUserContext
{
    string OperatorName { get; }
}

public sealed class WindowsUserContext : IUserContext
{
    public string OperatorName =>
        string.IsNullOrWhiteSpace(Environment.UserName) ? "Operator" : Environment.UserName;
}
