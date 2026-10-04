namespace Nova.Core.Tools;

public sealed record ClipboardItem(string Text, DateTimeOffset CopiedAt)
{
    public string Preview
    {
        get
        {
            var single = Text.ReplaceLineEndings(" ").Trim();
            return single.Length > 120 ? single[..117] + "…" : single;
        }
    }
}

/// <summary>In-memory (never persisted) list of recently copied text.</summary>
public sealed class ClipboardHistory
{
    private readonly List<ClipboardItem> _items = new();
    private readonly object _gate = new();

    public int Capacity { get; set; } = 10;
    public event Action? Changed;

    public IReadOnlyList<ClipboardItem> Items
    {
        get { lock (_gate) return _items.ToList(); }
    }

    public void Add(string text, DateTimeOffset? at = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > 100_000) text = text[..100_000];
        lock (_gate)
        {
            _items.RemoveAll(i => i.Text == text);
            _items.Insert(0, new ClipboardItem(text, at ?? DateTimeOffset.Now));
            if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        }
        Changed?.Invoke();
    }

    public void Remove(ClipboardItem item)
    {
        lock (_gate) _items.Remove(item);
        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) _items.Clear();
        Changed?.Invoke();
    }
}
