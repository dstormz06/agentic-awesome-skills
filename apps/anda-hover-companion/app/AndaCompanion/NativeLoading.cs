using System.Runtime.InteropServices;

// Every native call this app makes is to user32, kernel32 or gdi32. They are loaded from the Windows system folder only, never from the
// app's folder or the current folder, so a file with one of those names placed next to the app is not loaded in their place.
// A test (Round26SecurityTests) fails if a call to any other library is added without a decision here.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
