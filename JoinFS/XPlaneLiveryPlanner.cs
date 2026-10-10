using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JoinFS.Matching;

namespace JoinFS
{
    /// <summary>
    /// One 3D object of an aircraft as seen by a livery.
    /// </summary>
    /// <param name="RelativePath">The .obj, relative to the aircraft's objects folder, '/' separated</param>
    /// <param name="Texture">4th OBJ8 parameter: the livery's texture name, or the base texture when only the
    /// lit texture is replaced; null when nothing is overridden</param>
    /// <param name="Lit">5th OBJ8 parameter: the livery's lit texture name, or null</param>
    /// <param name="TextureSource">Livery file to copy for <paramref name="Texture"/>, or null</param>
    /// <param name="LitSource">Livery file to copy for <paramref name="Lit"/>, or null</param>
    internal sealed record LiveryObject(string RelativePath, string Texture, string Lit, string TextureSource, string LitSource);

    /// <summary>
    /// What one livery needs: its unique CSL id and the objects with their texture overrides.
    /// </summary>
    internal sealed record LiveryPlan(string FolderName, string Id, IReadOnlyList<LiveryObject> Objects);

    /// <summary>
    /// Plans the CSL models for an X-Plane aircraft's liveries. Only ever READS the aircraft folder:
    /// everything it describes is written by the caller into JoinFS's own CSL package.
    /// Not wrapped in a simulator #if so it can be unit tested in every configuration.
    /// </summary>
    internal static class XPlaneLiveryPlanner
    {
        /// <summary>The same exclusions the default model uses</summary>
        static readonly string[] SkippedObjectNames = { "pilot", "glass", "gear" };

        /// <summary>The JoinFS marker in the operator slot of a generated entry: "no airline"</summary>
        const string NoAirlineMarker = "JFS";

        /// <summary>Alternative texture formats X-Plane accepts for the same texture name</summary>
        static readonly string[] TextureExtensions = { ".png", ".dds" };

        /// <summary>
        /// CSL names (package, model, texture ids) may only contain letters, digits and underscores
        /// </summary>
        public static string SanitizeName(string name)
        {
            string result = "";
            foreach (char c in name ?? "")
            {
                if (c == ' ')
                {
                    result += '_';
                }
                else if (c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_')
                {
                    result += c;
                }
            }
            return result;
        }

        /// <summary>
        /// Names of the livery folders of an aircraft, sorted
        /// </summary>
        public static IReadOnlyList<string> EnumerateLiveries(string aircraftFolder)
        {
            string liveries = Path.Combine(aircraftFolder, "liveries");
            if (Directory.Exists(liveries) == false)
            {
                return [];
            }

            return Directory.GetDirectories(liveries)
                .Select(Path.GetFileName)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// A CSL-safe id for a livery that is unique among <paramref name="used"/>. Two folder names can
        /// sanitize to the same text, and XPMP2 ignores a second model with an id it already has.
        /// </summary>
        public static string UniqueId(string liveryName, ISet<string> used)
        {
            string baseId = SanitizeName(liveryName);
            if (baseId.Length == 0)
            {
                baseId = "livery";
            }

            string id = baseId;
            for (int suffix = 2; used.Contains(id); suffix++)
            {
                id = baseId + "_" + suffix;
            }
            used.Add(id);
            return id;
        }

        /// <summary>
        /// The TEXTURE and TEXTURE_LIT file names an .obj refers to; null for "none" or missing
        /// </summary>
        public static (string texture, string lit) ReadTextures(string objFile)
        {
            string texture = null;
            string lit = null;

            try
            {
                foreach (string line in File.ReadLines(objFile))
                {
                    string trimmed = line.Trim();
                    if (texture == null && TryValue(trimmed, "TEXTURE", out string value))
                    {
                        texture = value;
                    }
                    else if (lit == null && TryValue(trimmed, "TEXTURE_LIT", out string litValue))
                    {
                        lit = litValue;
                    }

                    if (texture != null && lit != null)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // an unreadable object just has no textures to replace
            }

            return (texture, lit);
        }

        static bool TryValue(string line, string command, out string value)
        {
            value = null;
            if (line.Length <= command.Length || line.StartsWith(command, StringComparison.Ordinal) == false || char.IsWhiteSpace(line[command.Length]) == false)
            {
                return false;
            }

            string text = line.Substring(command.Length).Trim();
            if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            value = text;
            return true;
        }

        /// <summary>
        /// The ICAO type of an .acf (its _ICAO property, at most 4 characters), "C172" when it has none -
        /// the same rule the default model uses
        /// </summary>
        public static string ReadIcaoType(string acfFile)
        {
            try
            {
                foreach (string line in File.ReadLines(acfFile))
                {
                    if (line.Contains("_ICAO"))
                    {
                        string[] words = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                        string type = words[^1];
                        return type.Length > 4 ? type.Substring(0, 4) : type;
                    }
                }
            }
            catch
            {
                // fall through to the default
            }
            return "C172";
        }

        /// <summary>
        /// The objects of an aircraft and which of their textures this livery replaces. Reads
        /// <c>&lt;aircraft&gt;\objects</c> and <c>&lt;aircraft&gt;\liveries\&lt;name&gt;\objects</c>; writes nothing.
        /// </summary>
        public static LiveryPlan Plan(string aircraftFolder, string liveryName, string id)
        {
            string objectsRoot = Path.Combine(aircraftFolder, "objects");
            string liveryRoot = Path.Combine(aircraftFolder, "liveries", liveryName, "objects");
            List<LiveryObject> objects = [];

            if (Directory.Exists(objectsRoot))
            {
                foreach (string objFile in Directory.GetFiles(objectsRoot, "*.obj", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (IsSkipped(objFile))
                    {
                        continue;
                    }
                    objects.Add(PlanObject(objectsRoot, liveryRoot, objFile, id));
                }
            }

            return new LiveryPlan(liveryName, id, objects);
        }

        static bool IsSkipped(string objFile)
        {
            string name = Path.GetFileName(objFile);
            return SkippedObjectNames.Any(skipped => name.Contains(skipped, StringComparison.OrdinalIgnoreCase));
        }

        static LiveryObject PlanObject(string objectsRoot, string liveryRoot, string objFile, string id)
        {
            string relative = Path.GetRelativePath(objectsRoot, objFile).Replace('\\', '/');
            string objectFolder = Path.GetDirectoryName(Path.GetRelativePath(objectsRoot, objFile)) ?? "";
            (string texture, string lit) = ReadTextures(objFile);

            (string textureTarget, string textureSource) = Override(liveryRoot, objectFolder, texture, id);
            (string litTarget, string litSource) = Override(liveryRoot, objectFolder, lit, id);

            // a lit override needs the 4th parameter too: keep the base texture there
            if (litTarget != null && textureTarget == null && texture != null)
            {
                textureTarget = texture;
            }
            // without any texture there is no 4th parameter to put the lit one behind
            if (textureTarget == null)
            {
                litTarget = null;
                litSource = null;
            }

            return new LiveryObject(relative, textureTarget, litTarget, textureSource, litSource);
        }

        /// <summary>
        /// The livery's replacement for one texture reference, if the livery ships one: the name to use
        /// (next to the .obj, prefixed with the livery id) and the file to copy
        /// </summary>
        static (string target, string source) Override(string liveryRoot, string objectFolder, string reference, string id)
        {
            if (reference == null || reference.StartsWith("..", StringComparison.Ordinal))
            {
                return (null, null);
            }

            string referencePath = reference.Replace('/', Path.DirectorySeparatorChar);
            foreach (string candidate in Candidates(referencePath))
            {
                string source = Path.Combine(liveryRoot, objectFolder, candidate);
                if (File.Exists(source))
                {
                    string folder = Path.GetDirectoryName(candidate)?.Replace('\\', '/') ?? "";
                    string name = id + "_" + Path.GetFileName(candidate);
                    return (folder.Length > 0 ? folder + "/" + name : name, source);
                }
            }
            return (null, null);
        }

        /// <summary>The texture as named, then with the other common extension</summary>
        static IEnumerable<string> Candidates(string referencePath)
        {
            yield return referencePath;

            string extension = Path.GetExtension(referencePath);
            foreach (string other in TextureExtensions.Where(e => e.Equals(extension, StringComparison.OrdinalIgnoreCase) == false))
            {
                yield return Path.ChangeExtension(referencePath, other);
            }
        }

        /// <summary>
        /// The airline's ICAO code for a livery folder name, or null. A leading 3-letter capital token
        /// that is an ICAO code ("DLH", "DLH_D-AIZZ"), or a name the airline directory knows
        /// ("Lufthansa", "Lufthansa 2019 D-AIZZ"). Names that merely look like a code ("Red") or
        /// match several airlines are not an airline.
        /// </summary>
        public static string ResolveAirline(string liveryName, AirlineResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(liveryName))
            {
                return null;
            }

            string name = liveryName.Trim();
            string firstToken = new string(name.TakeWhile(char.IsAsciiLetterOrDigit).ToArray());
            if (firstToken.Length == 3 && firstToken.All(char.IsAsciiLetterUpper) && resolver.IsKnownCode(firstToken))
            {
                return firstToken;
            }

            // "Red", "Sky", "Old": an ordinary word that happens to be an ICAO code
            if (name.Length == 3 && name.All(char.IsAsciiLetter))
            {
                return null;
            }

            AirlineResolution resolution = resolver.Resolve(name, "", name);
            return resolver.IsKnownCode(resolution.Icao) ? resolution.Icao.ToUpperInvariant() : null;
        }

        /// <summary>
        /// The xsb_aircraft.txt lines (a leading blank line included) that add this livery to a package.
        /// The objects point at the shared base folder; only textures the livery replaces are named.
        /// </summary>
        /// <param name="package">CSL package name (also the folder), used to qualify the model id</param>
        /// <param name="baseFolder">Folder of the shared base objects inside the package</param>
        /// <param name="icaoType">ICAO type written to the matching line</param>
        /// <param name="airline">ICAO airline code, or null for none</param>
        public static IReadOnlyList<string> BuildBlock(string package, string baseFolder, string icaoType, string airline, LiveryPlan plan)
        {
            string modelId = package + "_" + plan.Id;
            List<string> lines = ["", "OBJ8_AIRCRAFT " + modelId];

            foreach (LiveryObject obj in plan.Objects)
            {
                string line = "OBJ8 SOLID YES " + package + "/" + baseFolder + "/" + obj.RelativePath;
                if (obj.Texture != null)
                {
                    line += " " + obj.Texture;
                    if (obj.Lit != null)
                    {
                        line += " " + obj.Lit;
                    }
                }
                lines.Add(line);
            }

            lines.Add("LIVERY " + icaoType + " " + (airline ?? NoAirlineMarker) + " " + modelId);
            return lines;
        }
    }
}
