using ME3TweaksCore.Misc;
using System;
using System.Windows;
using System.Windows.Interop;

namespace ME3TweaksCoreWPF.UI
{
    /// <summary>
    /// Helper for taskbar operations, backed by a direct ITaskbarList3 COM interop shim
    /// (no WindowsAPICodePack / WinForms dependency). Designed to never throw out of these
    /// calls - COM creation can fail entirely under Wine, and the shell can throw internally
    /// on real Windows too.
    /// </summary>
    public static class TaskbarHelper
    {
        private static ITaskbarList3 _taskbarList;
        private static bool _initFailed;

        private static ITaskbarList3 GetTaskbarList()
        {
            if (_initFailed) return null;
            if (_taskbarList != null) return _taskbarList;

            try
            {
                var tbl = (ITaskbarList3)new TaskbarListCoClass();
                tbl.HrInit();
                _taskbarList = tbl;
                return _taskbarList;
            }
            catch
            {
                // CoCreateInstance can fail if explorer.exe isn't running, the component isn't
                // registered (some Wine setups), or we're not in a compatible apartment state.
                // Disable permanently for this process rather than retrying every call.
                _initFailed = true;
                return null;
            }
        }

        private static IntPtr GetMainWindowHandle()
        {
            try
            {
                var window = Application.Current?.MainWindow;
                if (window == null) return IntPtr.Zero;
                return new WindowInteropHelper(window).Handle; // throws if window not yet shown
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static void SetProgress(int currentvalue, int maxvalue)
        {
            try
            {
                var tbl = GetTaskbarList();
                var hwnd = GetMainWindowHandle();
                if (tbl == null || hwnd == IntPtr.Zero) return;
                tbl.SetProgressValue(hwnd, (ulong)currentvalue, (ulong)maxvalue);
            }
            catch
            {
                // Sometimes windows throws exception internally fetching progressbar and it bubbles out to here (yes, I've seen this)
            }
        }

        /// <summary>
        /// Sets the progress value. Value must be between 0 and 1
        /// </summary>
        public static void SetProgress(double progressVal)
        {
            SetProgress((int)(progressVal * 100), 100);
        }

        public static void SetProgressState(MTaskbarState state)
        {
            SetProgressState(ConvertTaskbarState(state));
        }

        public static void SetProgressState(TaskbarProgressBarState state)
        {
            try
            {
                var tbl = GetTaskbarList();
                var hwnd = GetMainWindowHandle();
                if (tbl == null || hwnd == IntPtr.Zero) return;
                tbl.SetProgressState(hwnd, state);
            }
            catch
            {
                // Sometimes windows throws exception internally fetching progressbar and it bubbles out to here (yes, I've seen this)
            }
        }

        private static TaskbarProgressBarState ConvertTaskbarState(MTaskbarState state)
        {
            switch (state)
            {
                case MTaskbarState.None: return TaskbarProgressBarState.NoProgress;
                case MTaskbarState.Progressing: return TaskbarProgressBarState.Normal;
                case MTaskbarState.Indeterminate: return TaskbarProgressBarState.Indeterminate;
                default: return TaskbarProgressBarState.NoProgress;
            }
        }
    }
}