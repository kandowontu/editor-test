using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FamidashEditor.MesenEmbedded;

internal static class MesenInterop
{
	private const string DllName = "MesenCore.dll";

	private static bool _resolverRegistered;

	public const uint EmulationFlag_ConsoleMode = 16u;

	public static string BundledMesenDir => Path.Combine(AppContext.BaseDirectory, "mesen");

	public static void EnsureResolver()
	{
		if (_resolverRegistered)
		{
			return;
		}
		_resolverRegistered = true;
		NativeLibrary.SetDllImportResolver(typeof(MesenInterop).Assembly, delegate(string name, Assembly asm, DllImportSearchPath? path)
		{
			if (string.Equals(name, "MesenCore.dll", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "MesenCore", StringComparison.OrdinalIgnoreCase))
			{
				string text = Path.Combine(BundledMesenDir, "MesenCore.dll");
				if (File.Exists(text))
				{
					return NativeLibrary.Load(text);
				}
			}
			return IntPtr.Zero;
		});
	}

	[DllImport("MesenCore.dll")]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool TestDll();

	[DllImport("MesenCore.dll")]
	public static extern void InitDll();

	[DllImport("MesenCore.dll")]
	public static extern void InitializeEmu([MarshalAs(UnmanagedType.LPUTF8Str)] string homeFolder, IntPtr windowHandle, IntPtr dxViewerHandle, [MarshalAs(UnmanagedType.I1)] bool useSoftwareRenderer, [MarshalAs(UnmanagedType.I1)] bool noAudio, [MarshalAs(UnmanagedType.I1)] bool noVideo, [MarshalAs(UnmanagedType.I1)] bool noInput);

	[DllImport("MesenCore.dll")]
	public static extern void Release();

	[DllImport("MesenCore.dll")]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool LoadRom([MarshalAs(UnmanagedType.LPUTF8Str)] string filepath, [MarshalAs(UnmanagedType.LPUTF8Str)] string? patchFile);

	[DllImport("MesenCore.dll")]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool IsRunning();

	[DllImport("MesenCore.dll")]
	public static extern void Stop();

	[DllImport("MesenCore.dll")]
	public static extern void Pause();

	[DllImport("MesenCore.dll")]
	public static extern void Resume();

	[DllImport("MesenCore.dll")]
	public static extern void SetRendererSize(uint width, uint height);

	[DllImport("MesenCore.dll")]
	public static extern int LoadScript([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string content, int scriptId);

	[DllImport("MesenCore.dll")]
	public static extern void RemoveScript(int scriptId);

	[DllImport("MesenCore.dll")]
	public static extern void InitializeDebugger();

	[DllImport("MesenCore.dll")]
	public static extern void ReleaseDebugger();

	[DllImport("MesenCore.dll")]
	[return: MarshalAs(UnmanagedType.I1)]
	public static extern bool IsDebuggerRunning();

	[DllImport("MesenCore.dll")]
	public static extern void SetEmulationFlag(uint flag, [MarshalAs(UnmanagedType.I1)] bool enabled);
}
