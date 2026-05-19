using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

public ref struct PathEnumerator(ReadOnlySpan<string> sourcePaths, string prefixSegment)
{
    private readonly ReadOnlySpan<string> _sourcePaths = sourcePaths;
    private readonly MapPath _prefixSegment = string.IsNullOrEmpty(prefixSegment) ? default : new MapPath(prefixSegment.AsSpan());
    private int _index = -1;
    private string _currentCombinedPathString = string.Empty;

    public readonly MapPath Current => new(_currentCombinedPathString.AsSpan());

    public bool MoveNext()
    {
        while (true)
        {
            _index++;
            if (_index >= _sourcePaths.Length) 
                return false;

            var localPathString = _sourcePaths[_index];
            if (string.IsNullOrEmpty(localPathString)) 
                continue;

            var localPath = new MapPath(localPathString.AsSpan());

            _currentCombinedPathString = _prefixSegment.IsEmpty
                ? localPath.ToString()
                : _prefixSegment + localPath;

            return true;
        }
    }
}
