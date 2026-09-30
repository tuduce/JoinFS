using System.Runtime.CompilerServices;
using System.Text;

namespace JoinFS.Tests.Legacy
{
    /// <summary>
    /// Compares captured datagrams against a checked-in hex fixture (one datagram per line) under
    /// JoinFS.Tests/Legacy/Fixtures/. Fixtures are located via the calling source file's path so
    /// update mode can write them back into the source tree.
    ///
    /// To (re)generate after an intentional wire change - which for the frozen legacy protocol
    /// should essentially never happen - run the tests with JOINFS_UPDATE_GOLDEN=1 and review the
    /// fixture diff like any other code change.
    /// </summary>
    static class GoldenFile
    {
        public static void Verify(string name, IReadOnlyList<byte[]> datagrams, [CallerFilePath] string callerPath = "")
        {
            Assert.NotEmpty(datagrams);

            string path = Path.Combine(Path.GetDirectoryName(callerPath)!, "Fixtures", name + ".hex");
            string actual = Format(name, datagrams);

            if (Environment.GetEnvironmentVariable("JOINFS_UPDATE_GOLDEN") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, actual);
                return;
            }

            Assert.True(File.Exists(path), $"Missing golden fixture {path} - run with JOINFS_UPDATE_GOLDEN=1 to create it.");
            string expected = File.ReadAllText(path).Replace("\r\n", "\n");
            Assert.Equal(expected, actual);
        }

        /// <summary>The datagrams stored in a fixture (for comparing another implementation against it).</summary>
        public static List<byte[]> Load(string name, [CallerFilePath] string callerPath = "")
        {
            string path = Path.Combine(Path.GetDirectoryName(callerPath)!, "Fixtures", name + ".hex");
            var datagrams = new List<byte[]>();
            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length > 0 && !line.StartsWith('#'))
                {
                    datagrams.Add(Convert.FromHexString(line));
                }
            }
            return datagrams;
        }

        static string Format(string name, IReadOnlyList<byte[]> datagrams)
        {
            var sb = new StringBuilder();
            sb.Append("# ").Append(name).Append(" - ").Append(datagrams.Count).Append(" datagram(s), legacy wire, one per line\n");
            foreach (byte[] d in datagrams)
            {
                sb.Append(Convert.ToHexString(d)).Append('\n');
            }
            return sb.ToString();
        }
    }
}
