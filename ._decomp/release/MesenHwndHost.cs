using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace FamidashEditor.MesenEmbedded;

internal sealed class MesenHwndHost : HwndHost
{
	private const int WS_CHILD = 1073741824;

	private const int WS_VISIBLE = 268435456;

	private const int WS_CLIPCHILDREN = 33554432;

	private const int WS_CLIPSIBLINGS = 67108864;

	public IntPtr Hwnd { get; private set; }

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

	[DllImport("user32.dll")]
	private static extern bool DestroyWindow(IntPtr hwnd);

	protected override HandleRef BuildWindowCore(HandleRef hwndParent)
	{
		Hwnd = CreateWindowExW(0, "STATIC", "MesenEmbeddedSurface", 1442840576, 0, 0, 256, 240, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
		if (Hwnd == IntPtr.Zero)
		{
			throw new InvalidOperationException("MesenHwndHost: CreateWindowExW failed (err=" + Marshal.GetLastWin32Error() + ").");
		}
		return new HandleRef(this, Hwnd);
	}

	protected override void DestroyWindowCore(HandleRef hwnd)
	{
		if (hwnd.Handle != IntPtr.Zero)
		{
			DestroyWindow(hwnd.Handle);
		}
		Hwnd = IntPtr.Zero;
	}
}
