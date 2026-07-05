using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Prius.Core.Maps;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Prius.Data.RavenDB.Metadata;

public sealed class MetadataProjectionContext : IMetadataProjectionContext
{
    private readonly string? _allowedNamespace;
    private readonly List<ProjectionAction> _actions = [];
    private readonly Dictionary<string, byte[]> _attachmentContents = new();
    private readonly Dictionary<string, IMap> _storedDocuments = new();

    public IReadOnlyList<ProjectionAction> Actions => _actions;

    public MetadataProjectionContext(string? allowedNamespace = null) => 
        _allowedNamespace = allowedNamespace;

    public void StoreDocument(string documentId, IMap documentData)
    {
        if (string.IsNullOrEmpty(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));

        if (_allowedNamespace is not null && !documentId.StartsWith(_allowedNamespace, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Handler is restricted to namespace '{_allowedNamespace}'. Cannot store document '{documentId}'.");

        _storedDocuments[documentId] = documentData;

        var docHash = ComputeMapHash(documentData);
        var action = new ProjectionAction(ProjectionActionType.StoreDocument, documentId, string.Empty, docHash);
        
        _actions.Add(action);
    }

    public void DeleteDocument(string documentId)
    {
        if (string.IsNullOrEmpty(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));

        if (_allowedNamespace is not null && !documentId.StartsWith(_allowedNamespace, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Handler is restricted to namespace '{_allowedNamespace}'. Cannot delete document '{documentId}'.");

        var action = new ProjectionAction(ProjectionActionType.DeleteDocument, documentId, string.Empty, string.Empty);
        
        _actions.Add(action);
    }

    public void StoreAttachment(string documentId, string attachmentName, Stream stream)
    {
        if (string.IsNullOrEmpty(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));

        if (string.IsNullOrEmpty(attachmentName))
            throw new ArgumentException("Attachment name cannot be null or empty.", nameof(attachmentName));

        if (_allowedNamespace is not null && !documentId.StartsWith(_allowedNamespace, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Handler is restricted to namespace '{_allowedNamespace}'. Cannot store attachment under '{documentId}'.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        var hashBytes = SHA256.HashData(bytes);
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var key = $"{documentId}:{attachmentName}";
        _attachmentContents[key] = bytes;

        var action = new ProjectionAction(ProjectionActionType.StoreAttachment, documentId, attachmentName, hash);
        
        _actions.Add(action);
    }

    public void DeleteAttachment(string documentId, string attachmentName)
    {
        if (string.IsNullOrEmpty(documentId))
            throw new ArgumentException("Document ID cannot be null or empty.", nameof(documentId));

        if (string.IsNullOrEmpty(attachmentName))
            throw new ArgumentException("Attachment name cannot be null or empty.", nameof(attachmentName));

        if (_allowedNamespace is not null && !documentId.StartsWith(_allowedNamespace, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Handler is restricted to namespace '{_allowedNamespace}'. Cannot delete attachment under '{documentId}'.");

        var action = new ProjectionAction(ProjectionActionType.DeleteAttachment, documentId, attachmentName, string.Empty);
        
        _actions.Add(action);
    }

    public string CalculateDerivedHash()
    {
        var sortedActions = _actions.OrderBy(a => a).ToList();
        var sb = new StringBuilder();
        
        foreach (var action in sortedActions)
        {
            sb.Append((int)action.Type).Append(':')
              .Append(action.DocumentId).Append(':')
              .Append(action.KeyOrName).Append(':')
              .Append(action.ValueHashOrContent).Append(';');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public async ValueTask FlushAsync(IAsyncDocumentSession session, CancellationToken ct = default)
    {
        var sortedActions = _actions.OrderBy(a => a).ToList();

        foreach (var action in sortedActions)
        {
            switch (action.Type)
            {
                case ProjectionActionType.StoreDocument:
                    if (_storedDocuments.TryGetValue(action.DocumentId, out var docMap))
                    {
                        var collection = GetCollectionName(action.DocumentId);
                        var rawMap = new DictionaryMap(docMap.DeepCopy());
                        var dict = rawMap.ToDictionary();

                        if (session.Advanced.IsLoaded(action.DocumentId))
                        {
                            var tracked = await session.LoadAsync<object>(action.DocumentId, ct);
                            if (tracked is not null)
                                session.Advanced.Evict(tracked);
                        }

                        await session.StoreAsync(dict, action.DocumentId, ct);
                        
                        var metadata = session.Advanced.GetMetadataFor(dict);
                        metadata["@collection"] = collection;
                    }
                    break;

                case ProjectionActionType.DeleteDocument:
                    session.Delete(action.DocumentId);
                    break;

                case ProjectionActionType.StoreAttachment:
                    var key = $"{action.DocumentId}:{action.KeyOrName}";
                    if (_attachmentContents.TryGetValue(key, out var bytes))
                    {
                        var doc = await session.LoadAsync<object>(action.DocumentId, ct);
                        if (doc is null)
                            throw new InvalidOperationException($"Cannot attach '{action.KeyOrName}' because document '{action.DocumentId}' does not exist.");

                        var stream = new MemoryStream(bytes);
                        session.Advanced.Attachments.Store(doc, action.KeyOrName, stream, "application/octet-stream");
                    }
                    break;

                case ProjectionActionType.DeleteAttachment:
                    var docToDeleteFrom = await session.LoadAsync<object>(action.DocumentId, ct);
                    if (docToDeleteFrom is not null)
                        session.Advanced.Attachments.Delete(docToDeleteFrom, action.KeyOrName);
                    break;
            }
        }
    }

    private static string GetCollectionName(string documentId)
    {
        var slashIdx = documentId.IndexOf('/');
        if (slashIdx > 0)
            return documentId.Substring(0, slashIdx);
        return "Documents";
    }

    public static string ComputeMapHash(IMap map)
    {
        var sb = new StringBuilder();
        AppendMap(map, sb);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void AppendMap(IMap map, StringBuilder sb)
    {
        sb.Append('{');
        var keys = map.Keys(ascending: true).ToList();
        var first = true;
        
        foreach (var key in keys)
        {
            if (!first)
                sb.Append(',');
            first = false;
            sb.Append('"').Append(key).Append("\":");
            AppendValue(map[key], sb);
        }
        
        sb.Append('}');
    }

    private static void AppendValue(MapValue val, StringBuilder sb)
    {
        val.Switch(
            onEmpty: _ => sb.Append("null"),
            onMap: m => AppendMap(m, sb),
            onString: s => sb.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'),
            onLong: l => sb.Append(l),
            onBool: b => sb.Append(b ? "true" : "false"),
            onDecimal: d => sb.Append(d.ToString(CultureInfo.InvariantCulture)),
            onDateTimeOffset: dto => sb.Append('"').Append(dto.ToString("O")).Append('"')
        );
    }
}

public enum ProjectionActionType
{
    StoreDocument = 1,
    DeleteDocument = 2,
    StoreAttachment = 3,
    DeleteAttachment = 4
}

public sealed class ProjectionAction : IComparable<ProjectionAction>
{
    public ProjectionActionType Type { get; }
    public string DocumentId { get; }
    public string KeyOrName { get; }
    public string ValueHashOrContent { get; }

    public ProjectionAction(ProjectionActionType type, string documentId, string keyOrName, string valueHashOrContent)
    {
        Type = type;
        DocumentId = documentId;
        KeyOrName = keyOrName;
        ValueHashOrContent = valueHashOrContent;
    }

    public int CompareTo(ProjectionAction? other)
    {
        if (other is null)
            return 1;

        var c1 = string.Compare(DocumentId, other.DocumentId, StringComparison.Ordinal);
        if (c1 != 0)
            return c1;

        var c2 = Type.CompareTo(other.Type);
        if (c2 != 0)
            return c2;

        var c3 = string.Compare(KeyOrName, other.KeyOrName, StringComparison.Ordinal);
        if (c3 != 0)
            return c3;

        return string.Compare(ValueHashOrContent, other.ValueHashOrContent, StringComparison.Ordinal);
    }
}
