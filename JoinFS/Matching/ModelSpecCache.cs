using System.Collections.Concurrent;
using System.Collections.Generic;
using Model = JoinFS.Substitution.Model;

namespace JoinFS.Matching
{
    /// <summary>
    /// Resolved physical data per kind of model, shared by every model of that kind. Thousands of installed models (liveries, AI traffic) share
    /// a few hundred types, so resolving once per type - not once per model per request - is what keeps matching cheap enough for the sim thread.
    /// The key is made of the values the resolution depends on, so a tag learned later (a changed ICAO type or class code) simply produces a new
    /// entry; the cache is dropped whenever the installed-model list is replaced.
    /// </summary>
    public sealed class ModelSpecCache
    {
        /// <summary>Everything <see cref="SpecResolver.ForModel"/> reads. Untagged models are resolved from their title, so it joins the key.</summary>
        readonly record struct Key(string IcaoType, string ClassCode, int Typerole, AircraftSpecs Measured, string TitleText);

        readonly SpecResolver resolver;
        readonly ConcurrentDictionary<Key, ResolvedSpecs> entries = new();
        IReadOnlyList<Model> list;

        public ModelSpecCache(SpecResolver resolver)
        {
            this.resolver = resolver;
        }

        public int Count => entries.Count;

        /// <summary>Forget everything when the model list was replaced (rescan, model removed, ...).</summary>
        public void Validate(IReadOnlyList<Model> models)
        {
            if (ReferenceEquals(list, models)) return;
            entries.Clear();
            list = models;
        }

        public ResolvedSpecs For(Model model)
        {
            Key key = new(model.icaoType, model.classCode, model.typerole, model.specs, model.icaoType.Length == 0 ? model.title + " " + model.variation : "");
            if (entries.TryGetValue(key, out ResolvedSpecs cached)) return cached;
            return entries.GetOrAdd(key, resolver.ForModel(model));
        }
    }
}
