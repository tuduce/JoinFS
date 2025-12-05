using JoinFS.DataModel;

namespace JoinFS.Tests;

/// <summary>
/// Minimal copy of Substitution.Model class for testing purposes.
/// This avoids dependency on the full JoinFS application.
/// </summary>
public static class Substitution
{
    public class Model
    {
        public string title;
        public string manufacturer;
        public string type;
        public string longType;
        public string variation;
        public int index;
        public string folder;
        public int typerole;
        public int smokeCount;
        public EnrichedAircraftData? enrichedData = null;
        public float[]? embedding = null;

        public Model(string title, string manufacturer, string type, string variation, int index, string typerole, string smoke, string folder)
        {
            this.title = title;
            this.manufacturer = manufacturer;
            this.type = type;
            longType = manufacturer + " " + type;
            this.variation = variation;
            this.index = index;
            this.folder = folder;
            this.typerole = TyperoleFromString(typerole);
            this.smokeCount = 0;
            int.TryParse(smoke, out this.smokeCount);
        }

        private static int TyperoleFromString(string typerole)
        {
            if (int.TryParse(typerole, out int result))
                return result;
            return 0;
        }
    }
}
