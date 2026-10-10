using Supermonitor.Core.Collections;

namespace Supermonitor.Tests.Collections;

public class RingBufferTests
{
    [Fact]
    public void Add_within_capacity_keeps_insertion_order()
    {
        var buffer = new RingBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);

        Assert.Equal(2, buffer.Count);
        Assert.Equal(1, buffer[0]);
        Assert.Equal(2, buffer[1]);
        Assert.Equal(2, buffer.Latest);
    }

    [Fact]
    public void Add_beyond_capacity_overwrites_oldest()
    {
        var buffer = new RingBuffer<int>(3);
        for (var i = 1; i <= 5; i++)
        {
            buffer.Add(i);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 3, 4, 5 }, new[] { buffer[0], buffer[1], buffer[2] });
    }

    [Fact]
    public void CopyLast_returns_most_recent_items_in_chronological_order()
    {
        var buffer = new RingBuffer<int>(4);
        for (var i = 1; i <= 6; i++)
        {
            buffer.Add(i);
        }

        Span<int> destination = stackalloc int[3];
        var copied = buffer.CopyLast(destination);

        Assert.Equal(3, copied);
        Assert.Equal(new[] { 4, 5, 6 }, destination.ToArray());
    }

    [Fact]
    public void CopyLast_with_larger_destination_copies_everything()
    {
        var buffer = new RingBuffer<int>(4);
        buffer.Add(7);
        buffer.Add(8);
        var destination = new int[10];

        Assert.Equal(2, buffer.CopyLast(destination));
        Assert.Equal(7, destination[0]);
        Assert.Equal(8, destination[1]);
    }

    [Fact]
    public void Indexer_out_of_range_throws()
    {
        var buffer = new RingBuffer<int>(2);
        buffer.Add(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[1]);
    }

    [Fact]
    public void Latest_on_empty_buffer_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new RingBuffer<int>(1).Latest);
    }

    [Fact]
    public void Capacity_below_one_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
    }

    [Fact]
    public void Clear_empties_the_buffer()
    {
        var buffer = new RingBuffer<int>(2);
        buffer.Add(1);
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
    }
}
