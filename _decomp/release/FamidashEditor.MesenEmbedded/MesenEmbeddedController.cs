using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FamidashEditor.MesenEmbedded;

internal static class MesenEmbeddedController
{
	private static readonly bool _skipLua = string.Equals(Environment.GetEnvironmentVariable("FAMIDASH_MESEN_NO_LUA"), "1", StringComparison.Ordinal);

	private static readonly bool _skipConsoleMode = string.Equals(Environment.GetEnvironmentVariable("FAMIDASH_MESEN_NO_CONSOLEMODE"), "1", StringComparison.Ordinal);

	private static readonly bool _skipSeedSize = string.Equals(Environment.GetEnvironmentVariable("FAMIDASH_MESEN_NO_SEEDSIZE"), "1", StringComparison.Ordinal);

	private static readonly bool _skipLoadRom = string.Equals(Environment.GetEnvironmentVariable("FAMIDASH_MESEN_NO_LOADROM"), "1", StringComparison.Ordinal);

	private static bool _initialized;

	private static int _luaScriptId = -1;

	private static readonly object _gate = new object();

	private static readonly string _stepLogPath = Path.Combine(Path.GetTempPath(), "famidash_mesen_embed.log");

	public static bool Initialized
	{
		get
		{
			lock (_gate)
			{
				return _initialized;
			}
		}
	}

	private static void LogStep(string msg)
	{
		try
		{
			File.AppendAllText(_stepLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
		}
		catch
		{
		}
	}

	public static void Initialize(IntPtr topLevelHwnd, IntPtr viewerHwnd, string homeFolder, bool noInput)
	{
		lock (_gate)
		{
			if (_initialized)
			{
				return;
			}
			LogStep($"Initialize.begin top=0x{topLevelHwnd.ToInt64():X} viewer=0x{viewerHwnd.ToInt64():X} home='{homeFolder}' noInput={noInput}");
			MesenInterop.EnsureResolver();
			LogStep("Initialize.resolver_ok");
			LogStep("Initialize.TestDll.pre");
			if (!MesenInterop.TestDll())
			{
				throw new InvalidOperationException("MesenCore.dll TestDll() returned false (DLL load issue?).");
			}
			LogStep("Initialize.TestDll.ok");
			Directory.CreateDirectory(homeFolder);
			LogStep("Initialize.InitDll.pre");
			MesenInterop.InitDll();
			LogStep("Initialize.InitDll.ok");
			LogStep("Initialize.InitializeEmu.pre");
			MesenInterop.InitializeEmu(homeFolder, topLevelHwnd, viewerHwnd, useSoftwareRenderer: false, noAudio: false, noVideo: false, noInput);
			LogStep("Initialize.InitializeEmu.ok");
			try
			{
				if (_skipConsoleMode)
				{
					LogStep("Initialize.SetEmulationFlag(ConsoleMode).skip FAMIDASH_MESEN_NO_CONSOLEMODE=1");
				}
				else
				{
					LogStep("Initialize.SetEmulationFlag(ConsoleMode).pre");
					MesenInterop.SetEmulationFlag(16u, enabled: true);
					LogStep("Initialize.SetEmulationFlag(ConsoleMode).ok");
				}
			}
			catch (Exception ex)
			{
				LogStep("Initialize.SetEmulationFlag.ex " + ex.GetType().Name + ": " + ex.Message);
			}
			try
			{
				if (_skipSeedSize)
				{
					LogStep("Initialize.SetRendererSize.default.skip FAMIDASH_MESEN_NO_SEEDSIZE=1");
				}
				else
				{
					LogStep("Initialize.SetRendererSize.default.pre 512x480");
					MesenInterop.SetRendererSize(512u, 480u);
					LogStep("Initialize.SetRendererSize.default.ok");
				}
			}
			catch (Exception ex2)
			{
				LogStep("Initialize.SetRendererSize.default.ex " + ex2.GetType().Name + ": " + ex2.Message);
			}
			_initialized = true;
		}
	}

	public static void SetRendererSize(int widthPx, int heightPx)
	{
		if (Initialized)
		{
			if (widthPx < 1)
			{
				widthPx = 1;
			}
			if (heightPx < 1)
			{
				heightPx = 1;
			}
			LogStep($"SetRendererSize.pre {widthPx}x{heightPx}");
			try
			{
				MesenInterop.SetRendererSize((uint)widthPx, (uint)heightPx);
			}
			catch (Exception ex)
			{
				LogStep("SetRendererSize.ex " + ex.GetType().Name + ": " + ex.Message);
			}
			LogStep("SetRendererSize.ok");
		}
	}

	public static bool LoadRom(string romPath)
	{
		if (!Initialized)
		{
			throw new InvalidOperationException("Mesen not initialized.");
		}
		if (string.IsNullOrEmpty(romPath) || !File.Exists(romPath))
		{
			throw new FileNotFoundException("ROM not found.", romPath);
		}
		LogStep("LoadRom.pre '" + romPath + "'");
		if (_skipLoadRom)
		{
			LogStep("LoadRom.skip FAMIDASH_MESEN_NO_LOADROM=1");
			return false;
		}
		bool flag = MesenInterop.LoadRom(romPath, null);
		LogStep($"LoadRom.ok result={flag}");
		if (flag)
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			while (stopwatch.ElapsedMilliseconds < 3000)
			{
				try
				{
					if (MesenInterop.IsRunning())
					{
						break;
					}
				}
				catch
				{
				}
				Thread.Sleep(10);
			}
			LogStep($"LoadRom.waitRunning elapsed={stopwatch.ElapsedMilliseconds}ms running={SafeIsRunning()}");
		}
		return flag;
	}

	private static bool SafeIsRunning()
	{
		try
		{
			return MesenInterop.IsRunning();
		}
		catch
		{
			return false;
		}
	}

	private static bool SafeIsDebuggerRunning()
	{
		try
		{
			return MesenInterop.IsDebuggerRunning();
		}
		catch
		{
			return false;
		}
	}

	public static void LoadOverlayLua(string luaPath, string? luaContent = null)
	{
		if (!Initialized)
		{
			return;
		}
		if (_skipLua)
		{
			LogStep("LoadOverlayLua.skip FAMIDASH_MESEN_NO_LUA=1");
			return;
		}
		try
		{
			if (!SafeIsRunning())
			{
				LogStep("LoadOverlayLua.skip emulator not running");
				return;
			}
			LogStep($"LoadOverlayLua.dbgRunning={SafeIsDebuggerRunning()}");
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(luaPath);
			string text = luaContent ?? File.ReadAllText(luaPath);
			UnloadOverlayLua();
			LogStep($"LoadScript.pre name='{fileNameWithoutExtension}' path='{luaPath}' contentLen={text.Length}");
			_luaScriptId = MesenInterop.LoadScript(fileNameWithoutExtension, luaPath, text, -1);
			LogStep($"LoadScript.ok id={_luaScriptId}");
		}
		catch (Exception ex)
		{
			LogStep("LoadOverlayLua.ex " + ex.GetType().Name + ": " + ex.Message);
		}
	}

	public static void UnloadOverlayLua()
	{
		if (_luaScriptId >= 0)
		{
			try
			{
				MesenInterop.RemoveScript(_luaScriptId);
			}
			catch
			{
			}
			_luaScriptId = -1;
		}
	}

	public static bool IsRunning()
	{
		if (Initialized)
		{
			return MesenInterop.IsRunning();
		}
		return false;
	}

	public static void Pause()
	{
		if (Initialized)
		{
			try
			{
				MesenInterop.Pause();
			}
			catch
			{
			}
		}
	}

	public static void Resume()
	{
		if (Initialized)
		{
			try
			{
				MesenInterop.Resume();
			}
			catch
			{
			}
		}
	}

	public static void Stop()
	{
		if (Initialized)
		{
			try
			{
				MesenInterop.Stop();
			}
			catch
			{
			}
		}
	}

	public static void Release()
	{
		lock (_gate)
		{
			if (_initialized)
			{
				UnloadOverlayLua();
				try
				{
					MesenInterop.Stop();
				}
				catch
				{
				}
				try
				{
					MesenInterop.Release();
				}
				catch
				{
				}
				_initialized = false;
			}
		}
	}
}
