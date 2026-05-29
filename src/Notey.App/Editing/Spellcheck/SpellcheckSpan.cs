namespace Notey.App.Editing.Spellcheck;

public sealed record SpellcheckSpan(int Offset, int Length, string Word)
{
    public int EndOffset => Offset + Length;

    public bool Contains(int offset)
    {
        return offset >= Offset && offset < EndOffset;
    }
}
