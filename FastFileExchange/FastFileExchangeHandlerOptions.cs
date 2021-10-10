using System.Text.RegularExpressions;

namespace FastFileExchange
{
    public sealed class FastFileExchangeHandlerOptions
    {
        public static readonly FastFileExchangeHandlerOptions Default = new();

        /// <summary>
        /// Files expire this long after the most recent access.
        /// </summary>
        public TimeSpan DefaultExpirationThreshold { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Files matching the patterns expire this long after the most recent access.
        /// Allows specific files to be preserved for longer/shorter than the default value.
        /// </summary>
        public Dictionary<Regex, TimeSpan> PatternSpecificExpirationThresholds { get; set; } = new();
    }
}
