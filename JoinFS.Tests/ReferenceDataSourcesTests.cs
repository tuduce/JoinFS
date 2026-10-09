using System.Text.Json;

namespace JoinFS.Tests
{
    /// <summary>
    /// aircraft-specs.json rows checked against manufacturer or specification sources say so: "verified": true needs an https "source".
    /// Rows without either are still compiled values that have not been checked.
    /// </summary>
    public class ReferenceDataSourcesTests
    {
        static JsonElement Rows()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "JoinFS", "Resources", "aircraft-specs.json"))) dir = Path.GetDirectoryName(dir)!;
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir ?? "", "JoinFS", "Resources", "aircraft-specs.json")));
            return doc.RootElement.GetProperty("aircraft").Clone();
        }

        static string Text(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

        [Fact]
        public void A_verified_row_names_an_https_source()
        {
            var offenders = Rows().EnumerateArray()
                .Where(r => r.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True)
                .Where(r => !Text(r, "source").StartsWith("https://", StringComparison.Ordinal))
                .Select(r => Text(r, "icao")).ToList();
            Assert.True(offenders.Count == 0, "verified rows without an https source: " + string.Join(", ", offenders));
        }

        [Fact]
        public void A_row_with_a_source_is_marked_verified()
        {
            var offenders = Rows().EnumerateArray()
                .Where(r => Text(r, "source").Length > 0)
                .Where(r => !(r.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True))
                .Select(r => Text(r, "icao")).ToList();
            Assert.True(offenders.Count == 0, "rows with a source but not marked verified: " + string.Join(", ", offenders));
        }

        [Fact]
        public void Some_rows_have_been_verified()
        {
            Assert.True(Rows().EnumerateArray().Count(r => Text(r, "source").Length > 0) >= 50);
        }
    }
}
