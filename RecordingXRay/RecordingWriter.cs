namespace RecordingXRay;

/// <summary>
/// Serialises a (potentially edited) <see cref="RecordingFile"/> back to the JoinFS
/// binary format.  Call <see cref="RecordingEditSession.CommitEditsToObject"/> on all
/// active sessions before invoking <see cref="Write"/>.
/// </summary>
public static class RecordingWriter
{
    /// <summary>
    /// Saves <paramref name="recording"/> to <paramref name="filePath"/>.
    /// </summary>
    /// <exception cref="NotImplementedException">
    /// Full binary serialisation is not yet implemented.  Wire your export pipeline here.
    /// </exception>
    public static void Write(RecordingFile recording, string filePath)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        // TODO: Implement the inverse of RecordingReader — write version header,
        //       aircraft list, object list and all frame types in the JoinFS binary format.
        throw new NotImplementedException(
            "RecordingWriter.Write is not yet implemented. " +
            "Implement binary serialisation here and call this method from the save hook.");
    }
}
