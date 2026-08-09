using System.Collections.ObjectModel;

namespace RecordingXRay;

public enum VariableKind { Integer, Float, String8 }

public enum RangeEditMode { ConstantOverwrite, LinearInterpolation }

public sealed class VariableEditEntry
{
    public required object Value { get; set; }
}

public sealed class FrameEditOverlay
{
    public Dictionary<uint, VariableEditEntry> IntOverrides { get; } = [];
    public Dictionary<uint, VariableEditEntry> FloatOverrides { get; } = [];
    public Dictionary<uint, VariableEditEntry> StringOverrides { get; } = [];

    public bool HasAnyOverride =>
        IntOverrides.Count > 0 || FloatOverrides.Count > 0 || StringOverrides.Count > 0;
}

/// <summary>
/// Tracks per-frame variable edits on top of an immutable <see cref="RecordedObject"/>.
/// Call <see cref="CommitEditsToObject"/> to materialise pending edits back into the
/// frames list before export.
/// </summary>
public sealed class RecordingEditSession
{
    private readonly RecordedObject _source;
    private readonly Dictionary<int, FrameEditOverlay> _overlays = [];

    public event EventHandler? Changed;

    public RecordingEditSession(RecordedObject source) => _source = source;

    public RecordedObject Source => _source;

    public bool HasEdits => _overlays.Values.Any(o => o.HasAnyOverride);

    public bool IsFrameDirty(int frameIndex) =>
        _overlays.TryGetValue(frameIndex, out FrameEditOverlay? o) && o.HasAnyOverride;

    public bool IsIntVarDirty(int frameIndex, uint varId) =>
        _overlays.TryGetValue(frameIndex, out FrameEditOverlay? o) &&
        o.IntOverrides.ContainsKey(varId);

    public bool IsFloatVarDirty(int frameIndex, uint varId) =>
        _overlays.TryGetValue(frameIndex, out FrameEditOverlay? o) &&
        o.FloatOverrides.ContainsKey(varId);

    public bool IsStringVarDirty(int frameIndex, uint varId) =>
        _overlays.TryGetValue(frameIndex, out FrameEditOverlay? o) &&
        o.StringOverrides.ContainsKey(varId);

    /// <summary>
    /// Returns the index of the most recent frame of <paramref name="type"/> at or before
    /// <paramref name="beforeOrAt"/>, or -1 if none exists.
    /// </summary>
    public int FindMostRecentFrameOfType(int beforeOrAt, FrameType type)
    {
        int limit = Math.Clamp(beforeOrAt, 0, _source.Frames.Count - 1);
        for (int i = limit; i >= 0; i--)
        {
            if (_source.Frames[i].Type == type) return i;
        }
        return -1;
    }

    // ── Effective value accessors (overlay first, then original frame) ──────────

    public int? GetEffectiveInt(int frameIndex, uint varId)
    {
        if (_overlays.TryGetValue(frameIndex, out FrameEditOverlay? ov) &&
            ov.IntOverrides.TryGetValue(varId, out VariableEditEntry? e))
            return (int)e.Value;

        return _source.Frames[frameIndex] is IntegerVariablesFrame f &&
               f.Variables.TryGetValue(varId, out int v) ? v : null;
    }

    public float? GetEffectiveFloat(int frameIndex, uint varId)
    {
        if (_overlays.TryGetValue(frameIndex, out FrameEditOverlay? ov) &&
            ov.FloatOverrides.TryGetValue(varId, out VariableEditEntry? e))
            return (float)e.Value;

        return _source.Frames[frameIndex] is FloatVariablesFrame f &&
               f.Variables.TryGetValue(varId, out float v) ? v : null;
    }

    public string? GetEffectiveString(int frameIndex, uint varId)
    {
        if (_overlays.TryGetValue(frameIndex, out FrameEditOverlay? ov) &&
            ov.StringOverrides.TryGetValue(varId, out VariableEditEntry? e))
            return (string)e.Value;

        return _source.Frames[frameIndex] is String8VariablesFrame f &&
               f.Variables.TryGetValue(varId, out string? v) ? v : null;
    }

    // ── Single-frame setters ─────────────────────────────────────────────────────

    public void SetInt(int frameIndex, uint varId, int value)
    {
        ValidateFrame<IntegerVariablesFrame>(frameIndex);
        GetOrCreateOverlay(frameIndex).IntOverrides[varId] = new VariableEditEntry { Value = value };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFloat(int frameIndex, uint varId, float value)
    {
        ValidateFrame<FloatVariablesFrame>(frameIndex);
        GetOrCreateOverlay(frameIndex).FloatOverrides[varId] = new VariableEditEntry { Value = value };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetString(int frameIndex, uint varId, string value)
    {
        ValidateFrame<String8VariablesFrame>(frameIndex);
        GetOrCreateOverlay(frameIndex).StringOverrides[varId] = new VariableEditEntry { Value = value };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ── Range appliers ───────────────────────────────────────────────────────────

    public void ApplyRangeInt(int startFrame, int endFrame, uint varId,
                              RangeEditMode mode, int startValue, int endValue = 0)
    {
        for (int i = startFrame; i <= endFrame && i < _source.Frames.Count; i++)
        {
            if (_source.Frames[i] is not IntegerVariablesFrame f || !f.Variables.ContainsKey(varId))
                continue;

            int v = ComputeValue(mode, startValue, endValue, startFrame, endFrame, i);
            GetOrCreateOverlay(i).IntOverrides[varId] = new VariableEditEntry { Value = v };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyRangeFloat(int startFrame, int endFrame, uint varId,
                                RangeEditMode mode, float startValue, float endValue = 0f)
    {
        for (int i = startFrame; i <= endFrame && i < _source.Frames.Count; i++)
        {
            if (_source.Frames[i] is not FloatVariablesFrame f || !f.Variables.ContainsKey(varId))
                continue;

            float v = (float)Lerp(startValue, endValue,
                mode == RangeEditMode.LinearInterpolation && endFrame > startFrame
                    ? (double)(i - startFrame) / (endFrame - startFrame)
                    : 0);
            GetOrCreateOverlay(i).FloatOverrides[varId] = new VariableEditEntry { Value = v };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyRangeString(int startFrame, int endFrame, uint varId, string value)
    {
        for (int i = startFrame; i <= endFrame && i < _source.Frames.Count; i++)
        {
            if (_source.Frames[i] is not String8VariablesFrame f || !f.Variables.ContainsKey(varId))
                continue;

            GetOrCreateOverlay(i).StringOverrides[varId] = new VariableEditEntry { Value = value };
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ── Commit ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materialises all overlay edits into <see cref="Source"/>.Frames by replacing
    /// each modified frame with a new immutable instance containing merged values.
    /// </summary>
    public void CommitEditsToObject()
    {
        foreach ((int fi, FrameEditOverlay overlay) in _overlays)
        {
            if (fi >= _source.Frames.Count || !overlay.HasAnyOverride) continue;

            RecordedFrame original = _source.Frames[fi];

            if (original is IntegerVariablesFrame intFrame && overlay.IntOverrides.Count > 0)
            {
                Dictionary<uint, int> merged = new(intFrame.Variables);
                foreach ((uint k, VariableEditEntry v) in overlay.IntOverrides)
                    merged[k] = (int)v.Value;

                _source.Frames[fi] = new IntegerVariablesFrame
                {
                    Type = intFrame.Type,
                    Time = intFrame.Time,
                    Variables = new ReadOnlyDictionary<uint, int>(merged)
                };
            }
            else if (original is FloatVariablesFrame floatFrame && overlay.FloatOverrides.Count > 0)
            {
                Dictionary<uint, float> merged = new(floatFrame.Variables);
                foreach ((uint k, VariableEditEntry v) in overlay.FloatOverrides)
                    merged[k] = (float)v.Value;

                _source.Frames[fi] = new FloatVariablesFrame
                {
                    Type = floatFrame.Type,
                    Time = floatFrame.Time,
                    Variables = new ReadOnlyDictionary<uint, float>(merged)
                };
            }
            else if (original is String8VariablesFrame stringFrame && overlay.StringOverrides.Count > 0)
            {
                Dictionary<uint, string> merged = new(stringFrame.Variables);
                foreach ((uint k, VariableEditEntry v) in overlay.StringOverrides)
                    merged[k] = (string)v.Value;

                _source.Frames[fi] = new String8VariablesFrame
                {
                    Type = stringFrame.Type,
                    Time = stringFrame.Time,
                    Variables = new ReadOnlyDictionary<uint, string>(merged)
                };
            }
        }

        _overlays.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private FrameEditOverlay GetOrCreateOverlay(int fi)
    {
        if (!_overlays.TryGetValue(fi, out FrameEditOverlay? overlay))
        {
            overlay = new FrameEditOverlay();
            _overlays[fi] = overlay;
        }
        return overlay;
    }

    private void ValidateFrame<T>(int fi) where T : RecordedFrame
    {
        if ((uint)fi >= (uint)_source.Frames.Count)
            throw new ArgumentOutOfRangeException(nameof(fi));

        if (_source.Frames[fi] is not T)
            throw new InvalidOperationException(
                $"Frame {fi} is not a {typeof(T).Name}.");
    }

    private static int ComputeValue(RangeEditMode mode, int start, int end,
                                    int rangeStart, int rangeEnd, int current)
    {
        if (mode == RangeEditMode.LinearInterpolation && rangeEnd > rangeStart)
            return (int)Math.Round(Lerp(start, end, (double)(current - rangeStart) / (rangeEnd - rangeStart)));
        return start;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
}
