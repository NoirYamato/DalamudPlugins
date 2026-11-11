namespace MitCue.Util;

/// <summary>Fixed-capacity buffer keeping the most recent items (oldest first when indexed). Not thread-safe.</summary>
/// <typeparam name="T">Item type.</typeparam>
internal sealed class RingBuffer<T>
{
    private readonly T[] items;
    private int start;

    /// <summary>Creates a buffer holding at most <paramref name="capacity"/> items.</summary>
    public RingBuffer(int capacity)
    {
        this.items = new T[Math.Max(1, capacity)];
    }

    /// <summary>Number of items currently held.</summary>
    public int Count { get; private set; }

    /// <summary>Maximum number of items.</summary>
    public int Capacity => this.items.Length;

    /// <summary>The item at <paramref name="index"/>, 0 being the oldest.</summary>
    public T this[int index] => this.items[(this.start + index) % this.items.Length];

    /// <summary>Adds an item, dropping the oldest when full.</summary>
    public void Add(T item)
    {
        if (this.Count < this.items.Length)
        {
            this.items[(this.start + this.Count) % this.items.Length] = item;
            this.Count++;
        }
        else
        {
            this.items[this.start] = item;
            this.start = (this.start + 1) % this.items.Length;
        }
    }

    /// <summary>Removes all items.</summary>
    public void Clear()
    {
        Array.Clear(this.items);
        this.start = 0;
        this.Count = 0;
    }
}
