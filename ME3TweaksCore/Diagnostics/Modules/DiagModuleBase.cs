using ME3TweaksCore.Diagnostics.Support;

namespace ME3TweaksCore.Diagnostics.Modules
{
    /// <summary>
    /// Interface for a diagnostic module.
    /// </summary>
    public abstract class DiagModuleBase
    {
        /// <summary>
        /// Runs the diagnostic module.
        /// </summary>
        internal abstract void RunModule(LogUploadPackage package);

        /// <summary>
        /// Invoked after RunModule either runs or fails. Used to perform cleanup regardless if the module crashes out.
        /// </summary>
        internal virtual void PostRunModule(LogUploadPackage package) { }
    }
}
