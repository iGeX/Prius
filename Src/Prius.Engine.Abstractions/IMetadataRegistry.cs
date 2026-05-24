using System;
using System.Threading;
using System.Threading.Tasks;
using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

/// <summary>
/// Provides a storage abstraction for the declarative application blueprint (routes and static environment).
/// </summary>
public interface IMetadataRegistry
{
    /// <summary>
    /// Retrieves the application blueprint containing route definitions and static environments.
    /// </summary>
    ValueTask<IMap> GetBlueprint(CancellationToken ct = default);
    
    event Func<ValueTask> OnTransitionToStasis;

    event Func<ValueTask> OnTransitionToActive;
    
    event Func<ValueTask> OnTransitionToTerminated;
}