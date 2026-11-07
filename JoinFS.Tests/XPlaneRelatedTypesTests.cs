namespace JoinFS.Tests
{
    /// <summary>
    /// XPMP2_related.dat is handed to the XPMP2 plugin (as related.txt), which uses it to pick a stand-in when an aircraft is not installed. The types the reference data
    /// knows from the helicopter add-ons (Cowan Simulation, Taog's Hangar) have to be in a group there, next to the ones they stand in for.
    /// </summary>
    public class XPlaneRelatedTypesTests
    {
        static List<string[]> Groups()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "JoinFS", "Resources", "XPMP2_related.dat"))) dir = Path.GetDirectoryName(dir)!;
            return File.ReadAllLines(Path.Combine(dir ?? "", "JoinFS", "Resources", "XPMP2_related.dat"))
                .Where(l => !l.TrimStart().StartsWith(";")).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(g => g.Length > 0).ToList();
        }

        [Theory]
        [InlineData("B22U", "B222")]
        [InlineData("B222", "B06")]
        [InlineData("ALO3", "LAMA")]
        [InlineData("AS65", "EC45")]
        [InlineData("UH1", "H500")]
        [InlineData("AS50", "B06")]
        public void A_helicopter_of_the_add_ons_has_a_group_with_the_type_it_stands_in_for(string type, string standIn)
            => Assert.Contains(Groups(), g => g.Contains(type) && g.Contains(standIn));

        [Fact]
        public void Every_type_appears_in_one_group_only()
        {
            var twice = Groups().SelectMany(g => g.Distinct()).GroupBy(t => t).Where(x => x.Count() > 1).Select(x => x.Key).ToList();
            Assert.True(twice.Count == 0, "in more than one group: " + string.Join(", ", twice));
        }
    }
}
