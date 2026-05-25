namespace Prius.Engine.Abstractions;

public interface IIntent
{
    IElementContext Context { get; }
    
    string SuccessPath { get; }
    
    string FailurePath { get; }
    
    CancellationToken Token { get; }
}
