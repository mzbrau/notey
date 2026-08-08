namespace Notey.App.ScreenshotEditor;

public sealed class ScreenshotEditHistory
{
    private const int MaxEntries = 50;
    private readonly List<EditSnapshot> _undo = [];

    public bool CanUndo => _undo.Count > 0;

    public void Push(ScreenshotEditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _undo.Add(document.CreateSnapshot());
        if (_undo.Count > MaxEntries)
        {
            _undo.RemoveAt(0);
        }
    }

    public bool TryUndo(ScreenshotEditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_undo.Count == 0)
        {
            return false;
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        document.RestoreSnapshot(snapshot);
        return true;
    }

    public void Clear() => _undo.Clear();
}
