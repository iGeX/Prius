namespace Prius.Engine.Abstractions;

public interface IIntent
{
    IReactorContext Context { get; }
    
    string SuccessPath { get; }
    
    string FailurePath { get; }
    
    CancellationToken Token { get; }
}
