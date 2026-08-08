using System;
using System.Runtime.InteropServices;

// This file is so we don't have to use WindowsAPICodePack
namespace ME3TweaksCoreWPF.UI
{

    /// <summary>
    /// Mirrors the native TBPFLAG enum used by ITaskbarList3::SetProgressState.
    /// Replaces WindowsAPICodePack's TaskbarProgressBarState so we don't need WinForms.
    /// </summary>
    [Flags]
    public enum TaskbarProgressBarState
    {
        NoProgress = 0,
        Indeterminate = 0x1,
        Normal = 0x2,
        Error = 0x4,
        Paused = 0x8
    }

    // Vtable order must match the real interface starting from IUnknown,
    // but trailing members we never call (overlay icons, thumbnails, etc.)
    // can simply be omitted - COM interop only cares about declared order.
    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITaskbarList3
    {
        // ITaskbarList
        [PreserveSig] void HrInit();
        [PreserveSig] void AddTab(IntPtr hwnd);
        [PreserveSig] void DeleteTab(IntPtr hwnd);
        [PreserveSig] void ActivateTab(IntPtr hwnd);
        [PreserveSig] void SetActiveAlt(IntPtr hwnd);

        // ITaskbarList2
        [PreserveSig] void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        // ITaskbarList3 (the two members we actually need)
        [PreserveSig] void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        [PreserveSig] void SetProgressState(IntPtr hwnd, TaskbarProgressBarState tbpFlags);
    }

    [ComImport]
    [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class TaskbarListCoClass
    {
    }
}
