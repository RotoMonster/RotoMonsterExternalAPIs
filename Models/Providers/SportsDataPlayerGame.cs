using System.Collections.Generic;

namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// One player's line from one game.
    ///
    /// Stats is a dictionary rather than fixed properties on purpose. The four
    /// sports have almost nothing in common statistically, and a class with
    /// every column of every sport on it would be mostly nulls. The provider
    /// maps its own field names to ours before filling this, so the caller can
    /// write the values straight into the right table without knowing anything
    /// about the provider.
    /// </summary>
    public class SportsDataPlayerGame
    {
        public string PlayerId { get; set; }
        public string GameId { get; set; }
        public string TeamCode { get; set; }
        public string Position { get; set; }

        public Dictionary<string, double> Stats { get; set; }
            = new Dictionary<string, double>();

        public double Get(string name)
        {
            double value;
            return Stats.TryGetValue(name, out value) ? value : 0;
        }
    }
}
