using System.Collections.ObjectModel;

namespace JoinFS.UI.ViewModels;

public static class CollectionSync
{
    /// <summary>
    /// Brings <paramref name="visible"/> into the order and contents of <paramref name="wanted"/> with the fewest changes: items that stay
    /// are moved, never rebuilt, so what the user is doing with a row (an open row, a half-ticked box, the scroll position) survives a live refresh.
    /// </summary>
    public static void Reconcile<T>(ObservableCollection<T> visible, IReadOnlyList<T> wanted) where T : class
    {
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = visible.IndexOf(wanted[i]);
            if (at == i)
                continue;
            if (at < 0)
                visible.Insert(i, wanted[i]);
            else
                visible.Move(at, i);
        }

        while (visible.Count > wanted.Count)
            visible.RemoveAt(visible.Count - 1);
    }
}
