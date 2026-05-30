using System.Globalization;
using System.Text.RegularExpressions;

namespace RecordingXRay;

public sealed class VariableLookup
{
    private static readonly string[] KnownVariableFiles =
    [
        "Plane.txt",
        "Rotorcraft.txt",
        "SingleProp.txt",
        "MultiProp.txt",
        "SingleTurbine.txt",
        "TwinTurbine.txt",
        "Jet.txt",
        "Glider.txt",
        "GroundVehicle.txt",
        "Carrier.txt",
    ];

    private readonly Dictionary<uint, string> names = [];

    public int Count => names.Count;

    public static VariableLookup Create()
    {
        VariableLookup lookup = new();
        lookup.LoadFromDocumentsFolder();
        lookup.LoadFromSourceFileFallback();
        return lookup;
    }

    public string Resolve(uint vuid)
    {
        return names.TryGetValue(vuid, out string? name) ? name : "unknown";
    }

    private void LoadFromDocumentsFolder()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JoinFS", "Variables");
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.txt"))
        {
            foreach (string line in File.ReadLines(file))
            {
                AddVariableLine(line);
            }
        }
    }

    private void LoadFromSourceFileFallback()
    {
        string? variablesSourceFile = FindUpward("JoinFS", "Variables.cs");
        if (variablesSourceFile is null)
        {
            return;
        }

        Regex regex = new("writer\\.WriteLine\\(\"(?<line>.*)\"\\);", RegexOptions.Compiled);
        foreach (string sourceLine in File.ReadLines(variablesSourceFile))
        {
            Match match = regex.Match(sourceLine);
            if (!match.Success)
            {
                continue;
            }

            string line = match.Groups["line"].Value.Replace("\\\"", "\"");
            AddVariableLine(line);
        }
    }

    private void AddVariableLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        string[] parts = line.Split('|');
        if (parts.Length < 4)
        {
            return;
        }

        string dataRef = parts[1].Trim();
        string scName = parts[2].Trim().ToLowerInvariant();
        string units = parts[3].Trim().ToLowerInvariant();

        int mask = 0;
        int? maskBit = null;
        if (units.StartsWith("mask:", StringComparison.Ordinal) && uint.TryParse(units[5..], NumberStyles.Number, CultureInfo.InvariantCulture, out uint bit) && bit < 32)
        {
            maskBit = (int)bit;
            mask = 1 << (int)bit;
            units = "mask";
        }

        uint vuid = 0;
        uint aliasVuid = 0;

        if (scName.Length > 0)
        {
            vuid = mask == 0 ? CreateVuid(scName) : CreateVuid(scName + mask.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(dataRef))
            {
                aliasVuid = CreateVuid(dataRef);
            }
        }
        else if (!string.IsNullOrEmpty(dataRef))
        {
            vuid = CreateVuid(dataRef);
        }

        if (vuid == 0)
        {
            return;
        }

        string displayName = BuildDisplayName(scName, dataRef, units, maskBit);
        if (!names.ContainsKey(vuid))
        {
            names[vuid] = displayName;
        }

        if (aliasVuid != 0 && !names.ContainsKey(aliasVuid))
        {
            names[aliasVuid] = displayName + " [alias]";
        }

        if (maskBit.HasValue && scName.Length > 0)
        {
            uint maskVuid = CreateVuid(scName);
            if (!names.ContainsKey(maskVuid))
            {
                names[maskVuid] = scName + " [mask-source]";
            }
        }
    }

    private static string BuildDisplayName(string scName, string dataRef, string units, int? maskBit)
    {
        string baseName = scName.Length > 0 ? scName : dataRef;
        if (baseName.Length == 0)
        {
            baseName = "unnamed";
        }

        if (maskBit.HasValue)
        {
            return $"{baseName} [mask bit {maskBit.Value}]";
        }

        if (!string.IsNullOrWhiteSpace(units))
        {
            return $"{baseName} ({units})";
        }

        return baseName;
    }

    private static uint CreateVuid(string text)
    {
        uint vuid = HashString(text);
        return vuid == 0 ? 1 : vuid;
    }

    private static uint HashString(string str)
    {
        unchecked
        {
            uint hash1 = (5381u << 16) + 5381u;
            uint hash2 = hash1;

            for (int i = 0; i < str.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ str[i];
                if (i == str.Length - 1)
                {
                    break;
                }

                hash2 = ((hash2 << 5) + hash2) ^ str[i + 1];
            }

            return hash1 + (hash2 * 1566083941u);
        }
    }

    private static string? FindUpward(params string[] relativePath)
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            string candidate = Path.Combine([current.FullName, .. relativePath]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }
}
