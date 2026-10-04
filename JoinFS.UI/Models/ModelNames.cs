using System.Text.RegularExpressions;

namespace JoinFS.UI.Models;

public static partial class ModelNames
{
    /// <summary>"GC1a Swift (Factory) (D)" becomes "GC1a Swift (Factory)": the design drops the trailing livery suffix to get the original model.</summary>
    public static string StripVariantSuffix(string model) => VariantSuffix().Replace(model, "");

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex VariantSuffix();
}
