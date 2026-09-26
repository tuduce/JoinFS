namespace JoinFS.Tests
{
    /// <summary>
    /// Substitution's collections are copy-on-write (docs/sim-thread-architecture.md §8.3): the sim
    /// thread reads them without a lock while other threads replace them, so a published collection
    /// must never change.
    /// </summary>
    public class SubstitutionConcurrencyTests
    {
        static JoinFS.Substitution.Model Model(string title) => new(title, "Maker", "Type", "Livery", 0, "SingleProp", "0", "");

        [Fact]
        public void RemoveMatch_PublishesANewDictionary_AndLeavesThePreviousOneIntact()
        {
            var substitution = new JoinFS.Substitution(null!);
            substitution.matches = new() { ["A"] = Model("a"), ["B"] = Model("b") };
            Dictionary<string, JoinFS.Substitution.Model> before = substitution.matches;

            substitution.RemoveMatch("A");

            Assert.NotSame(before, substitution.matches);
            Assert.True(before.ContainsKey("A"));
            Assert.False(substitution.matches.ContainsKey("A"));
            Assert.True(substitution.matches.ContainsKey("B"));
        }

        [Fact]
        public void ReadersEnumerating_WhileWritersReplace_NeverSeeACollectionChange()
        {
            var substitution = new JoinFS.Substitution(null!);
            substitution.matches = Enumerable.Range(0, 200).ToDictionary(i => "M" + i, i => Model("m" + i));
            substitution.models = Enumerable.Range(0, 200).Select(i => Model("m" + i)).ToList();

            using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            Exception? failure = null;
            var reader = Task.Run(() =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        // "Collection was modified" would throw here if a published collection changed
                        foreach (var pair in substitution.matches) _ = pair.Value.title;
                        foreach (var model in substitution.models) _ = model.title;
                        _ = substitution.GetModel("m150");
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            int n = 0;
            while (!stop.IsCancellationRequested)
            {
                substitution.RemoveMatch("M" + (n % 200));
                substitution.MakeIcaoIndex();
                n++;
            }
            reader.Wait();

            Assert.Null(failure);
            Assert.True(n > 0);
        }
    }
}
