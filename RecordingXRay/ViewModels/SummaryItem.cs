namespace RecordingXRay.ViewModels;

/// <summary>
/// One label / value pair in the summary strip. <see cref="Extra"/> is a secondary value in muted text;
/// <see cref="IsPlaceholder"/> dims the value while no recording is loaded.
/// </summary>
public sealed record SummaryItem(string Label, string Value, string Extra = "", bool IsPlaceholder = false);
