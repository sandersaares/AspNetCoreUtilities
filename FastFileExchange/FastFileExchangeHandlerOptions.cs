using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FastFileExchange
{
    public sealed class FastFileExchangeHandlerOptions
    {
        public static readonly FastFileExchangeHandlerOptions Default = new();

        /// <summary>
        /// Files expire this long after the most recent access.
        /// </summary>
        public TimeSpan DefaultExpirationThreshold = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Files matching the patterns expire this long after the most recent access.
        /// Allows specific files to be preserved for longer/shorter than the default value.
        /// </summary>
        public Dictionary<Regex, TimeSpan> PatternSpecificExpirationThresholds = new();
    }
}
