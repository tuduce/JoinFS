using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JoinFS.Recording
{
    /// <summary>
    /// The command-line door of the importer (docs/jfs2/recorder-v2-design.md section 8): <c>JoinFS -convert a.jfs b.jfs</c> writes
    /// <c>a.jfs2</c> and <c>b.jfs2</c> beside the originals, which are never modified, and never overwrites an existing file.
    /// Runs without starting the application, so it needs no simulator and no network.
    /// </summary>
    public static class Jfs2Convert
    {
        /// <summary>The command-line option that selects this mode.</summary>
        public const string Option = "-convert";

        /// <summary>The files after the option when the command line asks for a conversion; null when it does not.</summary>
        public static IReadOnlyList<string> FilesOf(IReadOnlyList<string> args)
        {
            int at = args.ToList().FindIndex(a => a.StartsWith('-') && a.TrimStart('-').Equals(Option.TrimStart('-'), StringComparison.OrdinalIgnoreCase));
            return at < 0 ? null : [.. args.Skip(at + 1).Where(a => !a.StartsWith('-'))];
        }

        /// <summary>Converts each file; returns 0 when all succeeded, 1 otherwise. Reports to <paramref name="output"/>.</summary>
        public static int Run(IReadOnlyList<string> files, TextWriter output)
        {
            if (files.Count == 0)
            {
                output.WriteLine("Usage: JoinFS " + Option + " <recording.jfs> [<recording.jfs> ...]");
                output.WriteLine("Writes <recording>.jfs2 beside each file. The originals are not changed.");
                return 1;
            }

            int failures = 0;
            foreach (string file in files)
            {
                try
                {
                    output.WriteLine(Convert(file));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or EndOfStreamException)
                {
                    failures++;
                    output.WriteLine(file + ": not converted: " + ex.Message);
                }
            }
            return failures == 0 ? 0 : 1;
        }

        /// <summary>Converts one file and describes the result in a line.</summary>
        /// <exception cref="InvalidOperationException">The file is not a recording this release reads, or holds nothing to play.</exception>
        public static string Convert(string file)
        {
            List<Recorder.Obj> objects;
            using (FileStream stream = File.OpenRead(file))
            {
                objects = new Recorder(null).Parse(new BinaryReader(stream))
                    ?? throw new InvalidOperationException("this recording is from a version too old to read");
            }
            if (!objects.Any(obj => obj.frames.Count > 0))
            {
                throw new InvalidOperationException("the recording holds no frames");
            }

            // the old format does not say when it started; the file was last written when the recording ended
            double length = objects.Max(obj => obj.frames.Count == 0 ? 0 : obj.frames[^1].time);
            DateTime start = File.GetLastWriteTimeUtc(file).AddSeconds(-length);

            string folder = Path.GetDirectoryName(Path.GetFullPath(file));
            string path = Recorder.UniquePath(folder, Path.ChangeExtension(Path.GetFileName(file), Format.Jfs2.Extension));
            Jfs2Exporter.Result result = Jfs2Exporter.Write(path, objects, "JoinFS (converted from " + Path.GetFileName(file) + ")", start, _ => { });

            return Path.GetFileName(file) + " -> " + path + " (" + result.Tracks + " objects, " + result.Frames + " frames, "
                + (new FileInfo(file).Length / 1024) + " KB -> " + (new FileInfo(path).Length / 1024) + " KB)";
        }
    }
}
