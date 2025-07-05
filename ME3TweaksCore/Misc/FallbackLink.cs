using System;
using System.Collections.Generic;

namespace ME3TweaksCore.Misc
{
    /// <summary>
    /// Link pairs for fallback systems (GitHub fallback to ME3Tweaks typically)
    /// </summary>
    public class FallbackLink
    {
        /// <summary>
        /// If this URL should randomly pick one of the two links to reduce load
        /// </summary>
        public bool LoadBalancing { get; init; }
        public string MainURL { get; init; }
        public string FallbackURL { get; init; }

        /// <summary>
        /// Fetches in order all populated links.
        /// </summary>
        /// <returns></returns>
        public List<string> GetAllLinks()
        {
            var urls = new List<string>();
            if (LoadBalancing && new Random().Next(2) == 0)
            {
                // Load balance - swap url order
                if (FallbackURL != null) urls.Add(FallbackURL);
                if (MainURL != null) urls.Add(MainURL);
            }
            else
            {
                if (MainURL != null) urls.Add(MainURL);
                if (FallbackURL != null) urls.Add(FallbackURL);
            }

            return urls;
        }
    }
}
