using System;
using System.Collections.Generic;
using System.Text;

namespace ME3TweaksCore.Exceptions
{
    /// <summary>
    /// An exception type that indicates to calling code that it doesn't need to submit telemetry
    /// for this event, indicating submits a more detailed one of its own.
    /// </summary>
    public class NoTelemetryException : Exception
    {
        /// <summary>
        /// Creates an exception that will be filtered out if it bubbles up to telemetry handling
        /// </summary>
        /// <param name="message"></param>
        public NoTelemetryException(string message) : base(message) { }
    }

    /// <summary>
    /// Thrown when a Bio2DA merge encounters an incompatible DLC mod
    /// </summary>
    public class IncompatibleBio2DAMergeException : NoTelemetryException
    {
        public IncompatibleBio2DAMergeException(string message) : base(message) {}
    }
}
