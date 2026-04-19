using System.Runtime.CompilerServices;

namespace Prius.Core.Maps;

public ref struct MapPath
{
    private const char Separator = '/';
    private const int NotScanned = -2;
    private const int NotFound = -1;

    private readonly ReadOnlySpan<char> _path;

    private int _cachedSeparatorIndex;
    private int _cachedEscapeCount;

    public MapPath(ReadOnlySpan<char> raw)
    {
        _cachedSeparatorIndex = NotScanned;
        _cachedEscapeCount = 0;

        if (raw.IsEmpty)
        {
            _path = ReadOnlySpan<char>.Empty;
            return;
        }

        var start = 0;
        var end = raw.Length - 1;

        while (start <= end && char.IsWhiteSpace(raw[start]))
            start++;

        while (end >= start && char.IsWhiteSpace(raw[end]))
            end--;

        var leading = 0;
        while (start + leading <= end && raw[start + leading] == Separator)
            leading++;

        if (leading % 2 != 0)
            start++;

        var trailing = 0;
        while (end - trailing >= start && raw[end - trailing] == Separator)
            trailing++;

        if (trailing % 2 != 0)
            end--;

        _path = start <= end ? raw[start..(end + 1)] : ReadOnlySpan<char>.Empty;
    }

    public bool IsEmpty => _path.IsEmpty;
    
    public int Length => _path.Length;

    public string Head
    {
        get
        {
            if (IsEmpty)
                return string.Empty;

            EnsureScan();

            var rawHead = _cachedSeparatorIndex == NotFound ? _path : _path[.._cachedSeparatorIndex];
            
            var endIdx = rawHead.Length - 1;
            while (endIdx >= 0 && char.IsWhiteSpace(rawHead[endIdx]))
                endIdx--;

            if (endIdx < 0)
                return string.Empty;

            rawHead = rawHead[..(endIdx + 1)];

            if (_cachedEscapeCount == 0)
                return rawHead.ToString();

            var length = rawHead.Length - _cachedEscapeCount;
            var result = new string('\0', length);

            unsafe
            {
                fixed (char* destPtr = result)
                fixed (char* srcPtr = rawHead)
                {
                    var dIdx = 0;
                    for (var sIdx = 0; sIdx < rawHead.Length; sIdx++)
                    {
                        destPtr[dIdx++] = srcPtr[sIdx];
                        if (srcPtr[sIdx] == Separator && sIdx + 1 < rawHead.Length && srcPtr[sIdx + 1] == Separator)
                            sIdx++;
                    }
                }    
            }
            
            return result;
        }
    }

    public MapPath Tail
    {
        get
        {
            EnsureScan();

            if (_cachedSeparatorIndex == NotFound)
                return default;

            return new MapPath(_path[(_cachedSeparatorIndex + 1)..]);
        }
    }

    public bool IsHeadEquals(string segment)
    {
        if (IsEmpty)
            return false;

        EnsureScan();
        
        var rawHead = _cachedSeparatorIndex == NotFound ? _path : _path[.._cachedSeparatorIndex];
        
        var endIdx = rawHead.Length - 1;
        while (endIdx >= 0 && char.IsWhiteSpace(rawHead[endIdx]))
            endIdx--;

        if (endIdx < 0)
            return segment.Length == 0;

        var headSpan = rawHead[..(endIdx + 1)];
        
        if (_cachedEscapeCount == 0)
            return headSpan.Equals(segment.AsSpan(), StringComparison.Ordinal);

        return Head == segment;
    }

    public bool Equals(MapPath other) => _path.Equals(other._path, StringComparison.Ordinal);

    public override bool Equals(object? obj) => throw new NotSupportedException();

    public override int GetHashCode()
    {
        var hash = 0;
        for (var i = 0; i < _path.Length; i++) 
            hash = (hash * 31) + i;
        return hash;
    }

    public static implicit operator string(MapPath path) => path.ToString();

    public static implicit operator MapPath(string path) => new(path.AsSpan());
    
    public static implicit operator MapPath(ReadOnlySpan<char> path) => new(path);

    public static bool operator ==(MapPath left, MapPath right) => left.Equals(right);
    
    public static bool operator !=(MapPath left, MapPath right) => !left.Equals(right);

    public static bool operator ==(MapPath left, string right) => left._path.Equals(right.AsSpan(), StringComparison.Ordinal);
    
    public static bool operator !=(MapPath left, string right) => !(left == right);

    public static unsafe string operator +(MapPath left, MapPath right)
    {
        if (left.IsEmpty) return right.ToString();
        if (right.IsEmpty) return left.ToString();

        var totalLength = left.Length + 1 + right.Length;
        var result = new string('\0', totalLength);

        fixed (char* destPtr = result)
        fixed (char* leftPtr = left._path)
        fixed (char* rightPtr = right._path)
        {
            var dest = new Span<char>(destPtr, totalLength);
            var leftSpan = new ReadOnlySpan<char>(leftPtr, left.Length);
            var rightSpan = new ReadOnlySpan<char>(rightPtr, right.Length);

            leftSpan.CopyTo(dest);
            dest[left.Length] = Separator;
            rightSpan.CopyTo(dest[(left.Length + 1)..]);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureScan()
    {
        if (_cachedSeparatorIndex != NotScanned)
            return;

        var (index, escapes) = Scan(_path);
        _cachedSeparatorIndex = index;
        _cachedEscapeCount = escapes;
    }

    private static (int index, int escapes) Scan(ReadOnlySpan<char> span)
    {
        var escapeCount = 0;
        var iIdx = 0;

        while (iIdx < span.Length)
        {
            if (span[iIdx] == Separator)
            {
                if (iIdx + 1 < span.Length && span[iIdx + 1] == Separator)
                {
                    escapeCount++;
                    iIdx += 2;
                    continue;
                }
                return (iIdx, escapeCount);
            }
            iIdx++;
        }
        return (NotFound, escapeCount);
    }

    public override string ToString() => _path.ToString();
}
