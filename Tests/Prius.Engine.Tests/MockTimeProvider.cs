using Prius.Engine.Abstractions;

namespace Prius.Engine.Tests;

public class MockTimeProvider : ITimeProvider
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
}
