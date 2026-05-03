using System;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace PensionCompass.Services;

/// <summary>
/// WinUI 3 file/folder pickers don't know which window they belong to in a desktop app,
/// so we have to associate them with the main window's HWND before showing them.
/// </summary>
public static class WindowHelper
{
    public static IntPtr GetHandle(Window window) => WindowNative.GetWindowHandle(window);

    public static void Initialize<T>(T target, Window window) where T : class
        => InitializeWithWindow.Initialize(target, GetHandle(window));
}
