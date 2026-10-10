namespace Supermonitor.Core.Collections;

public sealed class RingBuffer<T> where T : struct
{
    private readonly T[] _items;
    private int _start;
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _items = new T[capacity];
    }

    public int Capacity => _items.Length;

    public int Count => _count;

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _items[(_start + index) % _items.Length];
        }
    }

    public T Latest => _count == 0
        ? throw new InvalidOperationException("Buffer is empty.")
        : this[_count - 1];

    public void Add(T item)
    {
        var index = (_start + _count) % _items.Length;
        _items[index] = item;
        if (_count < _items.Length)
        {
            _count++;
        }
        else
        {
            _start = (_start + 1) % _items.Length;
        }
    }

    public int CopyLast(Span<T> destination)
    {
        var copied = Math.Min(destination.Length, _count);
        var first = _count - copied;
        for (var i = 0; i < copied; i++)
        {
            destination[i] = this[first + i];
        }

        return copied;
    }

    public void Clear()
    {
        _start = 0;
        _count = 0;
    }
}
