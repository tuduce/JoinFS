using JoinFS.Recording.Format;

namespace JoinFS.Recording.Tool;

/// <summary>
/// <c>jfs2 dump|verify|repair</c>: looks into a .jfs2 file without playing it. Records stay opaque, as in the format library.
/// </summary>
public static class Program
{
    const string Usage = """
        Usage:
          jfs2 dump [--records] <file.jfs2>   list the header, kinds, tracks, attachments and chunks
          jfs2 verify <file.jfs2>             read every chunk and check its CRC; exit code 1 if anything is wrong
          jfs2 repair <file.jfs2> [-o <out>]  finish a file that was cut off (default out: <name>-repaired.jfs2); the original is not changed
        """;

    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length >= 2 && args[0] == "dump")
            {
                bool records = args.Contains("--records");
                string file = args.Skip(1).FirstOrDefault(a => a != "--records") ?? "";
                using RecordingReader reader = RecordingReader.Open(file);
                RecordingDump.Write(output, reader, records);
                return 0;
            }
            if (args.Length == 2 && args[0] == "verify")
            {
                return Verify(args[1], output);
            }
            if (args.Length >= 2 && args[0] == "repair")
            {
                return Repair(args, output);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Jfs2FormatException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
        error.WriteLine(Usage);
        return 2;
    }

    static int Verify(string file, TextWriter output)
    {
        using RecordingReader reader = RecordingReader.Open(file);
        int unreadable = 0;
        long records = 0;
        for (int i = 0; i < reader.Chunks.Count; i++)
        {
            if (reader.TryReadChunk(i, out RecordChunk? chunk))
            {
                records += chunk!.Records.Count;
            }
            else
            {
                unreadable++;
                output.WriteLine($"chunk #{i} at byte {reader.Chunks[i].Offset}: unreadable");
            }
        }
        foreach (Jfs2Damage damage in reader.Damage)
        {
            output.WriteLine($"damage at byte {damage.Offset}: {damage.Message}");
        }
        bool ok = reader.IsFinished && unreadable == 0 && reader.Damage.Count == 0;
        output.WriteLine($"{(ok ? "OK" : "NOT OK")}: {(reader.IsFinished ? "finished" : "not finished")}, {reader.Chunks.Count} chunks ({unreadable} unreadable), {records} records, {reader.Tracks.Count} tracks");
        return ok ? 0 : 1;
    }

    static int Repair(string[] args, TextWriter output)
    {
        string file = args[1];
        int o = Array.IndexOf(args, "-o");
        string target = o > 0 && o + 1 < args.Length
            ? args[o + 1]
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(file))!, Path.GetFileNameWithoutExtension(file) + "-repaired" + Path.GetExtension(file));
        if (File.Exists(target))
        {
            throw new IOException($"{target} exists; choose another name with -o");
        }
        File.Copy(file, target);
        try
        {
            RepairResult result = RecordingRepair.Repair(target);
            output.WriteLine($"{target}: {(result.WasFinished ? "was already finished" : "finished")}, {result.Chunks} chunks kept, {result.BytesDropped} bytes dropped, {result.TracksClosed} tracks closed, {result.Damage.Count} damaged places");
            return 0;
        }
        catch
        {
            try { File.Delete(target); } catch (IOException) { }
            throw;
        }
    }
}
