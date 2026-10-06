namespace JoinFS
{
    /// <summary>
    /// The remote aircraft as it reaches model matching - exactly the values <c>Substitution.Match</c> has always received
    /// (what the sender's <c>IdentityUpdate</c> carried, plus the local registration). Nothing here is new on the wire:
    /// <see cref="Livery"/> is only filled on builds whose <c>Match</c> has a livery parameter (FS2024) and stays empty elsewhere.
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
