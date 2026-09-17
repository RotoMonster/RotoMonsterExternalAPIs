namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    public class SportsDataInjury
    {
        /// <summary>What is wrong, in their words. Can be "unknown".</summary>
        public string Description { get; set; }

        /// <summary>Their availability word, e.g. OUT or QUESTIONABLE.</summary>
        public string PlayingProbability { get; set; }
    }
}
