using System.Collections.Generic;
using System.Text;
using Model = JoinFS.Substitution.Model;

namespace JoinFS.Matching
{
    /// <summary>
    /// What the new matcher needs from the installed-model list and the user's settings. <see cref="SubstitutionCatalog"/> serves it from the
    /// live <see cref="Substitution"/> collections; tests serve it from a plain list.
    /// </summary>
    public interface IMatchCatalog
    {
        IReadOnlyList<Model> Models { get; }
        /// <summary>The user's substitutions and default-model entries (matching - &lt;sim&gt;.txt)</summary>
        IReadOnlyDictionary<string, Model> Matches { get; }
        /// <summary>typerole -> key in <see cref="Matches"/> of that typerole's default model</summary>
        IReadOnlyDictionary<int, string> DefaultModels { get; }
        Model GetModel(string title);
        Model GetModel(string title, string variation);
        Doc8643 Doc8643 { get; }
        /// <summary>The (localized) display name of a typerole, as JoinFS shows it</summary>
        string RoleName(int role);
    }

    /// <summary>The embedded reference data, read once: Doc8643, aircraft specs, related types and airlines.</summary>
    public static class MatchingData
    {
        static readonly System.Lazy<Doc8643> doc8643 = new(() => Doc8643.FromLines(Lines(Properties.Resources_XPLANE.XPMP2_Doc8643)));
        static readonly System.Lazy<ReferenceSpecs> reference = new(() => ReferenceSpecs.FromBytes(Properties.Resources_XPLANE.Aircraft_Specs));
        static readonly System.Lazy<RelatedTypes> related = new(() => RelatedTypes.FromLines(Lines(Properties.Resources_XPLANE.XPMP2_related)));
        static readonly System.Lazy<AirlineResolver> airlines = new(() => AirlineResolver.FromLines(Lines(Properties.Resources_XPLANE.ICAO_Airlines)));

        public static Doc8643 Doc8643 => doc8643.Value;
        public static ReferenceSpecs Reference => reference.Value;
        public static RelatedTypes Related => related.Value;
        public static AirlineResolver Airlines => airlines.Value;

        /// <summary>The text lines of an embedded data file.</summary>
        public static IEnumerable<string> Lines(byte[] data)
        {
            using System.IO.StringReader reader = new(Encoding.UTF8.GetString(data));
            string line;
            while ((line = reader.ReadLine()) != null) yield return line;
        }
    }

    /// <summary>Serves the live <see cref="Substitution"/> state to the matcher. Reads the copy-on-write collections without locking, like <c>Match</c> does.</summary>
    public sealed class SubstitutionCatalog : IMatchCatalog
    {
        readonly Substitution substitution;

        public SubstitutionCatalog(Substitution substitution)
        {
            this.substitution = substitution;
        }

        public IReadOnlyList<Model> Models => substitution.models;
        public IReadOnlyDictionary<string, Model> Matches => substitution.matches;
        public IReadOnlyDictionary<int, string> DefaultModels => substitution.defaultModels;
        public Model GetModel(string title) => substitution.GetModel(title);
        public Model GetModel(string title, string variation) => substitution.GetModel(title, variation);
        public Doc8643 Doc8643 => MatchingData.Doc8643;
        public string RoleName(int role) => substitution.typeroleNames.TryGetValue(role, out var name) ? name : role.ToString();
    }
}
