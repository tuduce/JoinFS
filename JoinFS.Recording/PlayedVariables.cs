using System.Collections.Generic;

namespace JoinFS.Recording
{
    /// <summary>
    /// The variables a player has given one object so far, merged (spec 7.6), so it can give them again every <see cref="Interval"/>.
    /// </summary>
    /// <remarks>
    /// A file holds variables as changes only, but the simulator does not take every change: an aircraft it has just created reads its
    /// own values first and refuses others for a few seconds (<c>VariableMgr.Set</c>), so a gear lowered once at the start of a track
    /// was lost and the aircraft taxied with its gear up (field test 2026-10-15). The network sends the whole set five times a second,
    /// and the old recorder recorded it that often; giving the merged set again as often does the same for playback. A value the object
    /// already has costs nothing.
    /// </remarks>
    public sealed class PlayedVariables
    {
        /// <summary>How often the set is given again (seconds), as often as the network sends it.</summary>
        public const double Interval = 0.2;

        Dictionary<uint, int> integers;
        Dictionary<uint, float> floats;
        Dictionary<uint, string> strings;
        double next = double.MinValue;

        /// <summary>The merged values of each kind, or null for a kind with none.</summary>
        public Dictionary<uint, int> Integers => integers;
        public Dictionary<uint, float> Floats => floats;
        public Dictionary<uint, string> Strings => strings;

        /// <summary>Nothing played yet (playing starts again).</summary>
        public void Clear()
        {
            integers = null;
            floats = null;
            strings = null;
            next = double.MinValue;
        }

        /// <summary>A variables frame has been played.</summary>
        public void Merge(SceneVariablesFrame frame)
        {
            var (i, f, s) = frame.ByKind();
            SceneTrack.Merge(ref integers, i);
            SceneTrack.Merge(ref floats, f);
            SceneTrack.Merge(ref strings, s);
        }

        /// <summary>A jump put the object in this state: these are its variables now (copied, the state's are not changed).</summary>
        public void Set(SceneState state)
        {
            Clear();
            SceneTrack.Merge(ref integers, state.Integers);
            SceneTrack.Merge(ref floats, state.Floats);
            SceneTrack.Merge(ref strings, state.Strings);
        }

        /// <summary>Whether the set is to be given again at <paramref name="now"/> (seconds); if so, the next time is an interval on.</summary>
        public bool Due(double now)
        {
            if (now < next)
            {
                return false;
            }
            next = now + Interval;
            return integers != null || floats != null || strings != null;
        }
    }
}
