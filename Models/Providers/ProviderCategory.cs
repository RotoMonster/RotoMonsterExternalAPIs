namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// A scoring category as the provider defines it.
    ///
    /// Code is the provider's own stat identifier, which is what the caller
    /// matches on - RM looks up Category.YahooId, not the name. The name is
    /// carried for logging when a code matches nothing.
    /// </summary>
    public class ProviderCategory
    {
        public string Code { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Which kind of player the stat applies to, in provider codes, e.g.
        /// Yahoo's "B" and "P" for batters and pitchers. Used to skip stats
        /// that do not apply to the sport being imported.
        /// </summary>
        public string PositionType { get; set; }

        /// <summary>
        /// Shown in the provider's UI but not scored. Callers normally skip
        /// these rather than creating a category for them.
        /// </summary>
        public bool IsDisplayOnly { get; set; }

        /// <summary>
        /// Points awarded per unit of this stat, in a points league. Null in a
        /// categories league, which is also how the caller can tell the two
        /// apart when the provider is vague about it.
        /// </summary>
        public double? PointsPerStat { get; set; }
    }
}
