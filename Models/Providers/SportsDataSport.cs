namespace RotoMonsterExternalAPIs.Client.Models.Providers
{
    /// <summary>
    /// Which league we're asking about. A parameter rather than a separate
    /// interface per sport, because the providers use the same shape for all
    /// four and only the stat names differ.
    /// </summary>
    public enum SportsDataSport
    {
        NFL = 1,
        NBA = 2,
        MLB = 3,
        NHL = 4
    }
}
