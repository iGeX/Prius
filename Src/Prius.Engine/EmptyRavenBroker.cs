using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

internal sealed class EmptyRavenBroker : IRavenBroker
{
    public static EmptyRavenBroker Instance { get; } = new();
    
    private EmptyRavenBroker() { }
    
    public void Load(string id, MapPath outputPath, MapPath failurePath) { }
    
    public void Query(IMap queryMap, MapPath outputPath, MapPath failurePath) { }

    public void Store(string id, IMap map, MapPath failurePath, string? changeVector = null) { }
    
    public void Patch(string id, MapPath path, MapValue value, MapPath failurePath, string? changeVector = null) { }
    
    public void Delete(string id, MapPath failurePath, string? changeVector = null) { }
    
    public void Subscription(string subscriptionName, MapPath dataPath, MapPath failurePath) { }
    
    public void ExecuteNative(Func<IRavenNativeContext, Task> nativeAction) { }
}
