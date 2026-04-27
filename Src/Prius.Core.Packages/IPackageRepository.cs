using Prius.Core.Maps;

namespace Prius.Core.Packages;

/// <summary>
/// Provides a storage abstraction for NuGet package metadata.
/// All methods use <see cref="IMap"/> for batch communication to maintain high-performance data exchange.
/// </summary>
public interface IPackageRepository
{
    /// <summary>
    /// Retrieves all unique package identifiers available in the repository.
    /// Output: Map { "newtonsoft.json": true, "prius.core": true }
    /// </summary>
    ValueTask<IMap> GetPackages(CancellationToken ct = default);
    
    /// <summary>
    /// Retrieves a set of available versions for the requested package identifiers.
    /// </summary>
    /// <param name="tfm">The Target Framework Moniker (e.g., "net8.0", "netstandard2.1") to filter compatible versions.</param>
    /// <param name="ids">A map where keys are package IDs and values are 'true'. Example: { "newtonsoft.json": true }.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A map where each key is a package ID and each value is another map containing available versions as keys.
    /// Example: { "newtonsoft.json": { "13.0.1": true, "13.0.3": true } }.
    /// </returns>
    ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct = default);

    /// <summary>
    /// Retrieves full manifests for the specified package versions.
    /// </summary>
    /// <param name="tfm">The Target Framework Moniker (e.g., "net8.0") to extract framework-specific metadata.</param>
    /// <param name="packages">A map where keys are package IDs and values are specific version strings. Example: { "newtonsoft.json": "13.0.3" }.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A map where each key is a package ID and each value is the package manifest stored as a map.
    /// </returns>
    ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct = default);
    
    /// <summary>
    /// Opens a stream to access the binary content identified by its hash.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    /// <param name="hash">The SHA256 hash of the content.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default);
}
