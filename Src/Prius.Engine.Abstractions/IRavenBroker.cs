using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

public interface IRavenBroker
{
    void Load(string id, MapPath outputPath, MapPath failurePath);
    
    void Query(IMap queryMap, MapPath outputPath, MapPath failurePath);
    
    void Store(string id, IMap map, MapPath failurePath, string? changeVector = null);
    
    void Patch(string id, MapPath path, MapValue value, MapPath failurePath, string? changeVector = null);
    
    void Delete(string id, MapPath failurePath, string? changeVector = null);
    
    void Subscription(string subscriptionName, MapPath dataPath, MapPath failurePath);
    
    void ExecuteNative(Func<IRavenNativeContext, Task> nativeAction);
}
