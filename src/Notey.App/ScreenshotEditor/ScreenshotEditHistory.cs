namespace Notey.App.ScreenshotEditor;

public sealed class ScreenshotEditHistory
{
    private const int MaxEntries = 50;
    private readonly List<EditSnapshot> _undo = [];
    private readonly List<EditSnapshot> _redo = [];

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Push(ScreenshotEditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _undo.Add(document.CreateSnapshot());
        if (_undo.Count > MaxEntries)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    public bool TryUndo(ScreenshotEditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_undo.Count == 0)
        {
            return false;
        }

        _redo.Add(document.CreateSnapshot());
        if (_redo.Count > MaxEntries)
        {
            _redo.RemoveAt(0);
        }

        var snapshot = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        document.RestoreSnapshot(snapshot);
        return true;
    }

    public bool TryRedo(ScreenshotEditDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (_redo.Count == 0)
        {
            return false;
        }

        _undo.Add(document.CreateSnapshot());
        if (_undo.Count > MaxEntries)
        {
            _undo.RemoveAt(0);
        }

        var snapshot = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        document.RestoreSnapshot(snapshot);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
