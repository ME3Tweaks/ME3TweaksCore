using System;
using System.Collections.Generic;
using System.Text;

namespace ME3TweaksCore.Objects
{
    /// <summary>
    /// Contains information about showing a progress bar
    /// </summary>
    public class ProgressInfo
    {
        public string Status;
        public double Value;
        public bool Indeterminate;

        /// <summary>
        /// Invoked when progress has updated
        /// </summary>
        public Action<ProgressInfo> OnUpdate;
    }
}
