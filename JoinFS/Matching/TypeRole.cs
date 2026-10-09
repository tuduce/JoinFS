using System.Collections.Generic;

namespace JoinFS.Matching
{
    /// <summary>
    /// Type roles for the new matcher: the same values as <c>Substitution.TypeRole_*</c> (they travel over the wire, so they never change),
    /// plus the class-code derivation the matcher uses to overrule a wrong or missing role.
    /// </summary>
    public static class TypeRole
    {
        public const int SingleProp = Substitution.TypeRole_SingleProp;
        public const int TwinProp = Substitution.TypeRole_TwinProp;
        public const int Airliner = Substitution.TypeRole_Airliner;
        public const int Rotorcraft = Substitution.TypeRole_Rotorcraft;
        public const int Glider = Substitution.TypeRole_Glider;
        public const int Fighter = Substitution.TypeRole_Fighter;
        public const int Bomber = Substitution.TypeRole_Bomber;
        public const int FourProp = Substitution.TypeRole_FourProp;
        public const int Airship = Substitution.TypeRole_Airship;
        public const int Balloon = Substitution.TypeRole_Balloon;

        /// <summary>English names, for the explanation (the dialog shows the localized ones from the Substitution instance).</summary>
        public static readonly Dictionary<int, string> Names = new()
        {
            { SingleProp, "SingleProp" }, { TwinProp, "TwinProp" }, { Airliner, "Airliner" }, { Rotorcraft, "Rotorcraft" },
            { Glider, "Glider" }, { Fighter, "Fighter" }, { Bomber, "Bomber" }, { FourProp, "FourProp" },
            { Airship, "Airship" }, { Balloon, "Balloon" },
        };

        /// <summary>
        /// A typerole from an ICAO type designator and its Doc8643 classification - the same rules as
        /// <c>Substitution.TyperoleFromIcao</c>. 0 when no reliable classification can be made.
        /// </summary>
        public static int FromIcao(string icaoType, string classCode, string wtc)
        {
            switch (icaoType)
            {
                case "SHIP": return Airship;
                case "BALL": return Balloon;
                case "GLID":
                case "GLIM": return Glider;
                case "GYRO":
                case "UHEL": return Rotorcraft;
                case "ULAC": return SingleProp;
            }

            if (classCode.Length != 3) return 0;

            char platform = classCode[0];
            char engineType = classCode[2];

            if (platform is 'H' or 'G') return Rotorcraft;
            if (classCode is "L1P" or "S1P" or "A1P") return SingleProp;
            if (classCode is "L2P" or "S2P" or "A2P") return TwinProp;
            if (classCode is "L4P" or "L4T") return FourProp;
            if (platform == 'L' && engineType == 'J' && wtc is "M" or "H" or "J") return Airliner;
            return 0;
        }

        /// <summary>
        /// <see cref="FromIcao"/>, and where JoinFS has no role: three prop engines (L3P/L3T) count as TwinProp, five or more as FourProp.
        /// The class code always describes the airframe, so it overrules a stated typerole.
        /// </summary>
        public static int FromClassCode(string icaoType, string classCode, string wtc)
        {
            int role = FromIcao(icaoType, classCode, wtc);
            if (role != 0 || classCode.Length != 3) return role;
            if (classCode[0] is 'L' or 'S' or 'A' && classCode[2] is 'P' or 'T' && int.TryParse(classCode[1].ToString(), out int engines))
            {
                return engines >= 5 ? FourProp : engines == 3 ? TwinProp : 0;
            }
            return 0;
        }
    }
}
