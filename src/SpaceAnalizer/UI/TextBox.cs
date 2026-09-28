namespace SpaceAnalizer.UI;

/// <summary>Editing model of a single-line text field (caret, selection, clipboard-friendly operations).</summary>
public sealed class TextBox
{
    public string Text { get; private set; } = "";
    public int Caret { get; private set; }
    public int Anchor { get; private set; }
    public bool Focused;
    /// <summary>Horizontal scroll (pixels) so the caret stays visible in narrow boxes.</summary>
    public float Scroll;

    public bool HasSelection => Anchor != Caret;
    public int SelectionStart => Math.Min(Anchor, Caret);
    public int SelectionEnd => Math.Max(Anchor, Caret);
    public string SelectedText => Text[SelectionStart..SelectionEnd];

    public void SetText(string text)
    {
        Text = text;
        Caret = Anchor = text.Length;
        Scroll = 0;
    }

    public void Insert(string s)
    {
        s = Sanitize(s);
        if (s.Length == 0) return;
        DeleteSelection();
        Text = Text.Insert(Caret, s);
        Caret += s.Length;
        Anchor = Caret;
    }

    public void Backspace(bool word = false)
    {
        if (DeleteSelection()) return;
        if (Caret == 0) return;
        int to = word ? WordLeft(Caret) : PrevIndex(Caret);
        Text = Text.Remove(to, Caret - to);
        Caret = Anchor = to;
    }

    public void Delete(bool word = false)
    {
        if (DeleteSelection()) return;
        if (Caret >= Text.Length) return;
        int to = word ? WordRight(Caret) : NextIndex(Caret);
        Text = Text.Remove(Caret, to - Caret);
        Anchor = Caret;
    }

    public void Left(bool extend, bool word = false)
    {
        if (!extend && HasSelection) { Caret = Anchor = SelectionStart; return; }
        Caret = word ? WordLeft(Caret) : PrevIndex(Caret);
        if (!extend) Anchor = Caret;
    }

    public void Right(bool extend, bool word = false)
    {
        if (!extend && HasSelection) { Caret = Anchor = SelectionEnd; return; }
        Caret = word ? WordRight(Caret) : NextIndex(Caret);
        if (!extend) Anchor = Caret;
    }

    public void Home(bool extend)
    {
        Caret = 0;
        if (!extend) Anchor = 0;
    }

    public void End(bool extend)
    {
        Caret = Text.Length;
        if (!extend) Anchor = Caret;
    }

    public void SelectAll()
    {
        Anchor = 0;
        Caret = Text.Length;
    }

    public void SetCaret(int index, bool extend)
    {
        Caret = Math.Clamp(index, 0, Text.Length);
        if (Caret > 0 && Caret < Text.Length && char.IsLowSurrogate(Text[Caret])) Caret--;
        if (!extend) Anchor = Caret;
    }

    public bool DeleteSelection()
    {
        if (!HasSelection) return false;
        int s = SelectionStart;
        Text = Text.Remove(s, SelectionEnd - s);
        Caret = Anchor = s;
        return true;
    }

    int PrevIndex(int i)
    {
        if (i <= 0) return 0;
        i--;
        if (i > 0 && char.IsLowSurrogate(Text[i]) && char.IsHighSurrogate(Text[i - 1])) i--;
        return i;
    }

    int NextIndex(int i)
    {
        if (i >= Text.Length) return Text.Length;
        i++;
        if (i < Text.Length && char.IsLowSurrogate(Text[i]) && char.IsHighSurrogate(Text[i - 1])) i++;
        return i;
    }

    int WordLeft(int i)
    {
        while (i > 0 && !char.IsLetterOrDigit(Text[i - 1])) i--;
        while (i > 0 && char.IsLetterOrDigit(Text[i - 1])) i--;
        return i;
    }

    int WordRight(int i)
    {
        while (i < Text.Length && !char.IsLetterOrDigit(Text[i])) i++;
        while (i < Text.Length && char.IsLetterOrDigit(Text[i])) i++;
        return i;
    }

    static string Sanitize(string s)
    {
        Span<char> buffer = s.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        int n = 0;
        foreach (char c in s)
            if (!char.IsControl(c)) buffer[n++] = c;
        return new string(buffer[..n]);
    }
}
