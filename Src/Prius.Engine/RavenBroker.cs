namespace Prius.Engine;

using Core.Maps;
using Abstractions;

internal sealed class RavenBroker(IntentRegistry registry, ReactorContext currentContext) : IRavenBroker
{
    public void Load(string id, MapPath outputPath, MapPath failurePath) => 
        registry.RegisterLoad(id, outputPath.ToString(), failurePath.ToString(), currentContext);

    public void Query(IMap queryMap, MapPath outputPath, MapPath failurePath) => 
        registry.RegisterQuery(queryMap, outputPath.ToString(), failurePath.ToString(), currentContext);

    public void Store(string id, IMap map, MapPath failurePath, string? changeVector = null) => 
        registry.RegisterStore(id, map, changeVector, failurePath.ToString(), currentContext);

    public void Patch(string id, MapPath path, MapValue value, MapPath failurePath, string? changeVector = null) => 
        registry.RegisterPatch(id, path.ToString(), value, changeVector, failurePath.ToString(), currentContext);

    public void Delete(string id, MapPath failurePath, string? changeVector = null) => 
        registry.RegisterDelete(id, changeVector, failurePath.ToString(), currentContext);

    public void Subscription(string subscriptionName, MapPath dataPath, MapPath failurePath) => 
        registry.RegisterSubscription(subscriptionName, dataPath.ToString(), failurePath.ToString(), currentContext);

    public void ExecuteNative(System.Func<IRavenNativeContext, Task> nativeAction) => 
        throw new NotSupportedException();
}
