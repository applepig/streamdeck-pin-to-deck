namespace PinToDeck.Models
{
    /// <summary>
    /// Window cycling action when app is in foreground
    /// </summary>
    public enum WindowCyclingAction
    {
        /// <summary>
        /// Cycle through all windows of the same app
        /// </summary>
        SwitchWithinApp = 0,

        /// <summary>
        /// Switch to the last active window (Alt+Tab behavior)
        /// </summary>
        SwitchToLast = 1,

        /// <summary>
        /// Do nothing when app is already in foreground
        /// </summary>
        DoNothing = 2
    }
}
