namespace Prius.Engine.Abstractions;

public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

public sealed class DefaultTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
