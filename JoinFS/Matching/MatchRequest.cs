namespace JoinFS
{
    /// <summary>
    /// The remote aircraft as it reaches model matching - exactly the values <c>Substitution.Match</c> has always received
    /// (what the sender's <c>IdentityUpdate</c> carried, plus the local registration). Nothing here is new on the wire:
    /// <see cref="Livery"/> is only filled on builds whose <c>Match</c> has a livery parameter (FS2024) and stays empty elsewhere.
    /// The last four fields are local to the matcher (corrections it makes itself); they are never sent.
    /// </summary>
    public sealed class MatchRequest
    {
        public string Title = "";
        public string Livery = "";
        public string IcaoType = "";
        public string IcaoAirline = "";
        public string ClassCode = "";
        public string Wtc = "";
        public bool ClassCodeConfirmed = false;
        public int Typerole = Substitution.TypeRole_SingleProp;
        public string Registration = "";

        /// <summary>True on builds whose Match has a livery parameter (FS2024): the exact-title tiers then look up title AND livery</summary>
        public bool LiveryAware = false;

        /// <summary>Optional, for reports</summary>
        public string Callsign = "";
        /// <summary>atc_airline of the remote aircraft when known (not on the wire); often confused with the ICAO airline</summary>
        public string AtcAirline = "";
        /// <summary>True when <see cref="IcaoAirline"/> was inferred by the matcher rather than reported</summary>
        public bool IcaoAirlineGuessed = false;
        /// <summary>True when <see cref="Typerole"/> was derived by the matcher rather than stated by the sender</summary>
        public bool TyperoleDerived = false;

        public MatchRequest()
        {
        }

        public MatchRequest(string title, string livery, string icaoType, string icaoAirline, string classCode, string wtc,
            bool classCodeConfirmed, int typerole, string registration)
        {
            Title = title;
            Livery = livery;
            IcaoType = icaoType;
            IcaoAirline = icaoAirline;
            ClassCode = classCode;
            Wtc = wtc;
            ClassCodeConfirmed = classCodeConfirmed;
            Typerole = typerole;
            Registration = registration;
        }
    }
}
