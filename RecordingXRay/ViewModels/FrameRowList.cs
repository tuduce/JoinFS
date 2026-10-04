using System.Collections;

namespace RecordingXRay.ViewModels;

/// <summary>
/// A read-only, virtual list of <see cref="FrameRow"/> over a lane's frames, optionally narrowed to a set of frame
/// indexes. Rows are made when the list is indexed, so a lane with tens of thousands of frames costs nothing up front.
/// Implements the non-generic <see cref="IList"/> because that is what the list control indexes into.
/// </summary>
public sealed class FrameRowList : IReadOnlyList<FrameRow>, IList
{
    private readonly IReadOnlyList<RecordedFrame> frames;
    private readonly int[]? indexes;

    public static FrameRowList Empty { get; } = new([], null);

    /// <param name="indexes">Ascending frame indexes to show, or null for every frame.</param>
    public FrameRowList(IReadOnlyList<RecordedFrame> frames, int[]? indexes)
    {
        this.frames = frames;
        this.indexes = indexes;
    }

    public int Count => indexes?.Length ?? frames.Count;

    public FrameRow this[int position]
    {
        get
        {
            if ((uint)position >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            int frameIndex = FrameIndexAt(position);
            return new FrameRow(frameIndex, frames[frameIndex], frameIndex > 0 ? frames[frameIndex - 1].Time : null);
        }
    }

    /// <summary>Position of the row for this frame index, or -1 when that frame is not in the list.</summary>
    public int PositionOf(int frameIndex)
    {
        if (indexes is null)
        {
            return (uint)frameIndex < (uint)frames.Count ? frameIndex : -1;
        }

        int position = Array.BinarySearch(indexes, frameIndex);
        return position >= 0 ? position : -1;
    }

    /// <summary>Position of the first row whose frame time is at or after <paramref name="time"/>, or Count - 1 when none is.</summary>
    public int PositionAtOrAfter(double time)
    {
        int low = 0;
        int high = Count - 1;
        if (high < 0)
        {
            return -1;
        }

        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (frames[FrameIndexAt(mid)].Time < time)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    private int FrameIndexAt(int position) => indexes is null ? position : indexes[position];

    public IEnumerator<FrameRow> GetEnumerator()
    {
        for (int i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // Non-generic IList, read only.
    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => true;

    int ICollection.Count => Count;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    int IList.IndexOf(object? value) => value is FrameRow row ? PositionOf(row.Index) : -1;

    bool IList.Contains(object? value) => value is FrameRow row && PositionOf(row.Index) >= 0;

    void ICollection.CopyTo(Array array, int index)
    {
        foreach (FrameRow row in this)
        {
            array.SetValue(row, index++);
        }
    }

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();
}
