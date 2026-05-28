using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FamidashEditor;

public class FamiStudioIntegration
{
	private readonly object playLock = new object();

	private AssemblyLoadContext? alc;

	private string? famiFolder;

	private WaveOutEvent? output;

	private WaveStream? reader;

	private string? lastTempWav;

	private string? currentPlayingPath;

	private string? lastFmsPath;

	private int lastTrackIndex = -1;

	private double playbackRate = 1.0;

	public string? StatusMessage { get; private set; }

	public bool IsLoaded => alc != null;

	public bool IsPlaying
	{
		get
		{
			if (output != null)
			{
				return output.PlaybackState == PlaybackState.Playing;
			}
			return false;
		}
	}

	public bool IsPaused
	{
		get
		{
			if (output != null)
			{
				return output.PlaybackState == PlaybackState.Paused;
			}
			return false;
		}
	}

	public void Resume()
	{
		try
		{
			if (output != null && output.PlaybackState == PlaybackState.Paused)
			{
				output.Volume = 1f;
				output.Play();
			}
		}
		catch
		{
		}
	}

	public void Pause()
	{
		try
		{
			if (output != null && output.PlaybackState == PlaybackState.Playing)
			{
				output.Pause();
			}
		}
		catch
		{
		}
	}

	public void LoadFromFolder(string folder)
	{
		if (!Directory.Exists(folder))
		{
			throw new DirectoryNotFoundException(folder);
		}
		famiFolder = folder;
		string text = Path.Combine(folder, "FamiStudio.exe");
		string text2 = Path.Combine(folder, "FamiStudio.dll");
		try
		{
			alc = new AssemblyLoadContext("famistudio", isCollectible: true);
			if (File.Exists(text2))
			{
				alc.LoadFromAssemblyPath(text2);
				StatusMessage = "Loaded FamiStudio.dll";
			}
			else if (File.Exists(text))
			{
				alc.LoadFromAssemblyPath(text);
				StatusMessage = "Loaded FamiStudio.exe";
			}
			else
			{
				alc.Unload();
				alc = null;
				StatusMessage = "No FamiStudio assemblies found in folder";
			}
		}
		catch (Exception ex)
		{
			alc = null;
			StatusMessage = "Failed to load FamiStudio assemblies: " + ex.Message;
			throw;
		}
	}

	public List<string> EnumerateTracks(string fmsPath)
	{
		try
		{
			if (fmsPath != null && Path.GetExtension(fmsPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
			{
				List<string> list = ParseFamiStudioTextExport(fmsPath);
				if (list != null && list.Count > 0)
				{
					StatusMessage = $"Found {list.Count} tracks via text export";
					return list;
				}
			}
		}
		catch
		{
		}
		List<string> list2 = new List<string>();
		if (alc == null)
		{
			return list2;
		}
		try
		{
			object obj2 = null;
			foreach (Assembly assembly in alc.Assemblies)
			{
				Type[] types = assembly.GetTypes();
				Type[] array = types;
				foreach (Type type in array)
				{
					MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
					foreach (MethodInfo methodInfo in methods)
					{
						ParameterInfo[] parameters = methodInfo.GetParameters();
						if (parameters.Length != 1 || !(parameters[0].ParameterType == typeof(string)))
						{
							continue;
						}
						string text = methodInfo.Name.ToLowerInvariant();
						if (!text.Contains("load") && !text.Contains("open") && !text.Contains("fromfile") && !text.Contains("loadproject"))
						{
							continue;
						}
						try
						{
							if (methodInfo.IsStatic)
							{
								obj2 = methodInfo.Invoke(null, new object[1] { fmsPath });
							}
							else
							{
								object obj3 = Activator.CreateInstance(type);
								if (obj3 != null)
								{
									obj2 = methodInfo.Invoke(obj3, new object[1] { fmsPath });
								}
							}
						}
						catch
						{
							obj2 = null;
						}
						if (obj2 != null)
						{
							break;
						}
					}
					if (obj2 != null)
					{
						break;
					}
				}
				if (obj2 != null)
				{
					break;
				}
				array = types;
				foreach (Type type2 in array)
				{
					try
					{
						ConstructorInfo constructor = type2.GetConstructor(new Type[1] { typeof(string) });
						if (constructor != null)
						{
							try
							{
								obj2 = constructor.Invoke(new object[1] { fmsPath });
							}
							catch
							{
								obj2 = null;
							}
							if (obj2 != null)
							{
								break;
							}
						}
					}
					catch
					{
					}
				}
				if (obj2 != null)
				{
					break;
				}
			}
			if (obj2 == null)
			{
				return list2;
			}
			Type type3 = obj2.GetType();
			object obj7 = null;
			PropertyInfo propertyInfo = type3.GetProperty("Songs") ?? type3.GetProperty("SongList") ?? type3.GetProperty("Tracks");
			if (propertyInfo != null)
			{
				obj7 = propertyInfo.GetValue(obj2);
			}
			else
			{
				MethodInfo methodInfo2 = type3.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).FirstOrDefault((MethodInfo mi) => typeof(IEnumerable).IsAssignableFrom(mi.ReturnType) && mi.GetParameters().Length == 0);
				if (methodInfo2 != null)
				{
					try
					{
						obj7 = methodInfo2.Invoke(obj2, null);
					}
					catch
					{
						obj7 = null;
					}
				}
			}
			if (obj7 is IEnumerable enumerable && !(obj7 is string))
			{
				string text2 = null;
				Type type4 = null;
				foreach (object item in enumerable)
				{
					if (item == null)
					{
						continue;
					}
					type4 = item.GetType();
					PropertyInfo propertyInfo2 = type4.GetProperty("Name") ?? type4.GetProperty("Title") ?? type4.GetProperty("SongName") ?? type4.GetProperty("DisplayName");
					if (propertyInfo2 != null && propertyInfo2.PropertyType == typeof(string))
					{
						text2 = propertyInfo2.Name;
						break;
					}
					foreach (PropertyInfo item2 in from pp in type4.GetProperties()
						where pp.PropertyType == typeof(string)
						select pp)
					{
						string text3 = item2.Name.ToLowerInvariant();
						if (text3.Contains("name") || text3.Contains("title") || text3.Contains("display"))
						{
							text2 = item2.Name;
							break;
						}
					}
					if (!string.IsNullOrEmpty(text2))
					{
						break;
					}
					try
					{
						string text4 = item.ToString();
						if (!string.IsNullOrEmpty(text4) && text4.Length < 128 && Regex.IsMatch(text4, "[A-Za-z0-9]"))
						{
							text2 = "__tostring";
						}
					}
					catch
					{
					}
					break;
				}
				int num = 0;
				foreach (object item3 in enumerable)
				{
					if (item3 == null)
					{
						num++;
						continue;
					}
					string text5 = null;
					if (!string.IsNullOrEmpty(text2) && text2 != "__tostring")
					{
						try
						{
							PropertyInfo property = type4.GetProperty(text2);
							if (property != null)
							{
								text5 = property.GetValue(item3)?.ToString() ?? "";
							}
						}
						catch
						{
							text5 = null;
						}
					}
					else if (text2 == "__tostring")
					{
						try
						{
							text5 = item3.ToString();
						}
						catch
						{
							text5 = null;
						}
					}
					if (string.IsNullOrEmpty(text5))
					{
						text5 = $"Song {num}";
					}
					list2.Add(text5);
					num++;
				}
				StatusMessage = $"Found {list2.Count} tracks via in-process API";
				return list2;
			}
			PropertyInfo propertyInfo3 = type3.GetProperty("SongCount") ?? type3.GetProperty("TrackCount") ?? type3.GetProperty("SongsCount");
			if (propertyInfo3 != null && propertyInfo3.PropertyType == typeof(int))
			{
				object value = propertyInfo3.GetValue(obj2);
				if (value == null)
				{
					return list2;
				}
				int count = Convert.ToInt32(value);
				List<string> list3 = (from value2 in Enumerable.Range(0, count)
					select $"Song {value2}").ToList();
				StatusMessage = $"Found {list3.Count} tracks via in-process API";
				return list3;
			}
		}
		catch
		{
		}
		return list2;
	}

	public List<string> ParseFamiStudioTextExport(string path)
	{
		List<string> list = new List<string>();
		if (!File.Exists(path))
		{
			return list;
		}
		string[] array;
		try
		{
			array = File.ReadAllLines(path);
		}
		catch
		{
			return list;
		}
		Regex regex = new Regex("Name\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].TrimStart();
			if (!text.StartsWith("Song", StringComparison.OrdinalIgnoreCase) || (text.Length != 4 && !char.IsWhiteSpace(text[4]) && text[4] != '\t'))
			{
				continue;
			}
			string text2 = null;
			for (int j = i; j < Math.Min(array.Length, i + 24); j++)
			{
				string text3 = array[j];
				if (j > i && text3.Length > 0 && !char.IsWhiteSpace(text3[0]))
				{
					break;
				}
				Match match = regex.Match(text3);
				if (match.Success)
				{
					text2 = match.Groups[1].Value.Trim();
					if (!string.IsNullOrEmpty(text2) && !list.Contains(text2))
					{
						list.Add(text2);
					}
					break;
				}
			}
		}
		return list;
	}

	public List<string> ProbeTracksViaCli(string fmsPath, int maxTracks = 32)
	{
		List<string> list = new List<string>();
		if (famiFolder == null)
		{
			StatusMessage = "FamiStudio folder not configured";
			return list;
		}
		string text = Path.Combine(famiFolder, "FamiStudio.exe");
		if (!File.Exists(text))
		{
			StatusMessage = "FamiStudio.exe not found in bundled folder";
			return list;
		}
		int num = Math.Max(maxTracks, 512);
		int num2 = 0;
		for (int i = 0; i < num; i++)
		{
			string text2 = Path.Combine(Path.GetTempPath(), $"fms_probe_{Guid.NewGuid()}.wav");
			try
			{
				string arguments = $"\"{fmsPath}\" wav-export \"{text2}\" -export-songs:{i} -wav-export-rate:48000";
				using Process process = Process.Start(new ProcessStartInfo(text, arguments)
				{
					CreateNoWindow = true,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				});
				if (process == null)
				{
					continue;
				}
				process.WaitForExit(3000);
				if (File.Exists(text2) && new FileInfo(text2).Length > 100)
				{
					try
					{
						using AudioFileReader audioFileReader = new AudioFileReader(text2);
						if (audioFileReader.TotalTime.TotalSeconds >= 0.5)
						{
							list.Add($"Song {i}");
							num2 = 0;
						}
						else
						{
							num2++;
						}
					}
					catch
					{
						num2++;
					}
					try
					{
						File.Delete(text2);
					}
					catch
					{
					}
					if (num2 >= 12)
					{
						break;
					}
					continue;
				}
				try
				{
					if (File.Exists(text2))
					{
						File.Delete(text2);
					}
				}
				catch
				{
				}
				num2++;
				if (num2 < 12)
				{
					continue;
				}
				break;
			}
			catch
			{
				try
				{
					if (File.Exists(text2))
					{
						File.Delete(text2);
					}
				}
				catch
				{
				}
			}
		}
		StatusMessage = $"CLI probe found {list.Count} tracks";
		return list;
	}

	public List<string> TryParseFmsSongNames(string fmsPath)
	{
		List<string> list = new List<string>();
		try
		{
			byte[] bytes = File.ReadAllBytes(fmsPath);
			string text = Encoding.UTF8.GetString(bytes);
			if (string.IsNullOrEmpty(text))
			{
				return list;
			}
			foreach (Match item in Regex.Matches(text, "\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
			{
				string text2 = item.Groups[1].Value.Trim();
				if (!string.IsNullOrEmpty(text2) && !list.Contains(text2))
				{
					list.Add(text2);
				}
			}
			if (list.Count == 0)
			{
				foreach (Match item2 in Regex.Matches(text, "Song\\s*[:=]\\s*\"?([A-Za-z0-9 _-]{1,60})\"?", RegexOptions.IgnoreCase))
				{
					string text3 = item2.Groups[1].Value.Trim();
					if (!string.IsNullOrEmpty(text3) && !list.Contains(text3))
					{
						list.Add(text3);
					}
				}
			}
			return list;
		}
		catch
		{
			return list;
		}
	}

	public void PlayTrack(string fmsPath, int trackIndex)
	{
		lock (playLock)
		{
			lastFmsPath = fmsPath;
			lastTrackIndex = trackIndex;
			Stop();
			try
			{
				string text = FindExistingCachedMusic(fmsPath, trackIndex);
				if (!string.IsNullOrEmpty(text) && File.Exists(text))
				{
					PlayWav(text);
					StatusMessage = "Playing cached track";
					return;
				}
			}
			catch
			{
			}
			if (famiFolder == null)
			{
				StatusMessage = "FamiStudio not configured";
				throw new InvalidOperationException("FamiStudio folder not configured");
			}
			string text2 = Path.Combine(famiFolder, "FamiStudio.exe");
			if (!File.Exists(text2))
			{
				StatusMessage = "FamiStudio.exe not found in bundled folder";
				throw new FileNotFoundException("FamiStudio.exe not found", text2);
			}
			string text3 = Path.Combine(Path.GetTempPath(), $"fms_play_{Guid.NewGuid()}.wav");
			string arguments = $"\"{fmsPath}\" wav-export \"{text3}\" -export-songs:{trackIndex} -wav-export-rate:48000";
			using (Process process = Process.Start(new ProcessStartInfo(text2, arguments)
			{
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			}))
			{
				if (process == null)
				{
					throw new Exception("Failed to start FamiStudio CLI");
				}
				process.WaitForExit(60000);
			}
			if (!File.Exists(text3))
			{
				throw new Exception("Export failed or produced no WAV");
			}
			string cachedMusicPath = GetCachedMusicPath(fmsPath, trackIndex);
			string text4 = ConvertWavToCached(text3, cachedMusicPath);
			if (!string.IsNullOrEmpty(text4) && File.Exists(text4))
			{
				PlayWav(text4);
				lastTempWav = null;
				StatusMessage = "Playing (cached)";
			}
			else
			{
				PlayWav(text3);
				lastTempWav = text3;
				StatusMessage = "Playing (CLI fallback)";
			}
		}
	}

	private void PlayWav(string wavPath)
	{
		try
		{
			currentPlayingPath = wavPath;
		}
		catch
		{
			currentPlayingPath = wavPath;
		}
		try
		{
			reader = new AudioFileReader(wavPath);
		}
		catch
		{
			reader = new WaveFileReader(wavPath);
		}
		output = new WaveOutEvent();
		try
		{
			if (Math.Abs(playbackRate - 1.0) > 0.0001 && reader != null)
			{
				try
				{
					SampleToWaveProvider16 waveProvider = new SampleToWaveProvider16(new VarispeedSampleProvider(reader.ToSampleProvider(), playbackRate));
					output.Init(waveProvider);
				}
				catch
				{
					output.Init(reader);
				}
			}
			else
			{
				output.Init(reader);
			}
		}
		catch
		{
			try
			{
				output.Init(reader);
			}
			catch
			{
			}
		}
		output.PlaybackStopped += delegate
		{
			try
			{
				reader?.Dispose();
			}
			catch
			{
			}
			try
			{
				output?.Dispose();
			}
			catch
			{
			}
			reader = null;
			output = null;
			try
			{
				currentPlayingPath = null;
			}
			catch
			{
			}
			if (lastTempWav != null)
			{
				try
				{
					File.Delete(lastTempWav);
				}
				catch
				{
				}
				lastTempWav = null;
			}
		};
		output.Volume = 1f;
		output.Play();
	}

	private void PlayWavStream(Stream wavStream)
	{
		try
		{
			if (wavStream.CanSeek)
			{
				wavStream.Position = 0L;
			}
		}
		catch
		{
		}
		try
		{
			reader = new WaveFileReader(wavStream);
		}
		catch (Exception ex)
		{
			throw new Exception("In-process render did not produce a WAV stream: " + ex.Message);
		}
		output = new WaveOutEvent();
		try
		{
			if (Math.Abs(playbackRate - 1.0) > 0.0001 && reader != null)
			{
				try
				{
					WaveFormat outputFormat = new WaveFormat((int)((double)reader.WaveFormat.SampleRate * playbackRate), reader.WaveFormat.BitsPerSample, reader.WaveFormat.Channels);
					MediaFoundationResampler waveProvider = new MediaFoundationResampler(reader, outputFormat)
					{
						ResamplerQuality = 60
					};
					output.Init(waveProvider);
				}
				catch
				{
					output.Init(reader);
				}
			}
			else
			{
				output.Init(reader);
			}
		}
		catch
		{
			try
			{
				output.Init(reader);
			}
			catch
			{
			}
		}
		output.PlaybackStopped += delegate
		{
			try
			{
				reader?.Dispose();
			}
			catch
			{
			}
			try
			{
				output?.Dispose();
			}
			catch
			{
			}
			reader = null;
			output = null;
			if (lastTempWav != null)
			{
				try
				{
					File.Delete(lastTempWav);
				}
				catch
				{
				}
				lastTempWav = null;
			}
		};
		output.Volume = 1f;
		output.Play();
	}

	public void Stop()
	{
		lock (playLock)
		{
			try
			{
				if (output != null)
				{
					try
					{
						output.Volume = 0f;
						if (output.PlaybackState != PlaybackState.Stopped)
						{
							output.Stop();
						}
					}
					catch
					{
					}
					try
					{
						output.Dispose();
					}
					catch
					{
					}
				}
				try
				{
					reader?.Dispose();
				}
				catch
				{
				}
				reader = null;
				output = null;
				currentPlayingPath = null;
			}
			catch
			{
			}
			try
			{
				if (lastTempWav != null && File.Exists(lastTempWav))
				{
					File.Delete(lastTempWav);
				}
			}
			catch
			{
			}
			lastTempWav = null;
			StatusMessage = "Stopped";
		}
	}

	private string GetMusicCacheDir()
	{
		try
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Famidash Editor", "Music");
			if (!Directory.Exists(text))
			{
				Directory.CreateDirectory(text);
			}
			return text;
		}
		catch
		{
			return Path.GetTempPath();
		}
	}

	private string GetCachedMusicPath(string fmsPath, int trackIndex)
	{
		try
		{
			if (string.IsNullOrEmpty(fmsPath))
			{
				return null;
			}
			using SHA1 sHA = SHA1.Create();
			string s = fmsPath + "|" + trackIndex;
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			string value = BitConverter.ToString(sHA.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
			string path = $"track_{value}_{trackIndex}.mp3";
			return Path.Combine(GetMusicCacheDir(), path);
		}
		catch
		{
			return Path.Combine(Path.GetTempPath(), $"track_{trackIndex}.mp3");
		}
	}

	private string? FindExistingCachedMusic(string fmsPath, int trackIndex)
	{
		try
		{
			string cachedMusicPath = GetCachedMusicPath(fmsPath, trackIndex);
			if (File.Exists(cachedMusicPath))
			{
				return cachedMusicPath;
			}
			string text = Path.ChangeExtension(cachedMusicPath, ".wav");
			if (File.Exists(text))
			{
				return text;
			}
			return null;
		}
		catch
		{
			return null;
		}
	}

	private string EnsureCachedMusic(string fmsPath, int trackIndex)
	{
		string cachedMusicPath = GetCachedMusicPath(fmsPath, trackIndex);
		if (File.Exists(cachedMusicPath))
		{
			return cachedMusicPath;
		}
		if (famiFolder == null)
		{
			return string.Empty;
		}
		string text = Path.Combine(famiFolder, "FamiStudio.exe");
		if (!File.Exists(text))
		{
			return string.Empty;
		}
		string text2 = Path.Combine(Path.GetTempPath(), $"fms_export_{Guid.NewGuid()}.wav");
		string arguments = $"\"{fmsPath}\" wav-export \"{text2}\" -export-songs:{trackIndex} -wav-export-rate:48000";
		ProcessStartInfo startInfo = new ProcessStartInfo(text, arguments)
		{
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		try
		{
			using Process process = Process.Start(startInfo);
			process?.WaitForExit(15000);
		}
		catch
		{
		}
		if (!File.Exists(text2))
		{
			return string.Empty;
		}
		return ConvertWavToCached(text2, cachedMusicPath) ?? string.Empty;
	}

	private string ConvertWavToCached(string wavPath, string cachedTarget)
	{
		try
		{
			if (!File.Exists(wavPath))
			{
				return string.Empty;
			}
			try
			{
				using AudioFileReader inputProvider = new AudioFileReader(wavPath);
				try
				{
					MediaFoundationApi.Startup();
					MediaFoundationEncoder.EncodeToMp3(inputProvider, cachedTarget);
					try
					{
						File.Delete(wavPath);
					}
					catch
					{
					}
					return cachedTarget;
				}
				catch
				{
				}
			}
			catch
			{
			}
			try
			{
				string text = Path.ChangeExtension(cachedTarget, ".wav");
				File.Copy(wavPath, text, overwrite: true);
				try
				{
					File.Delete(wavPath);
				}
				catch
				{
				}
				return text;
			}
			catch
			{
				return string.Empty;
			}
		}
		catch
		{
			return string.Empty;
		}
	}

	public void SeekToPosition(double seconds)
	{
		try
		{
			lock (playLock)
			{
				if (reader == null)
				{
					return;
				}
				try
				{
					if (reader is AudioFileReader audioFileReader)
					{
						audioFileReader.CurrentTime = TimeSpan.FromSeconds(seconds);
						return;
					}
					long num = (long)(seconds * (double)reader.WaveFormat.AverageBytesPerSecond);
					if (reader.CanSeek && num >= 0 && num < reader.Length)
					{
						reader.Position = num;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	public void SetPlaybackRate(double rate)
	{
		try
		{
			if (rate <= 0.0)
			{
				return;
			}
			playbackRate = rate;
			lock (playLock)
			{
				try
				{
					if (output == null && reader == null)
					{
						return;
					}
					TimeSpan currentTime = TimeSpan.Zero;
					try
					{
						if (reader is AudioFileReader audioFileReader)
						{
							currentTime = audioFileReader.CurrentTime;
						}
					}
					catch
					{
						currentTime = TimeSpan.Zero;
					}
					try
					{
						output?.Stop();
					}
					catch
					{
					}
					try
					{
						output?.Dispose();
					}
					catch
					{
					}
					WaveStream waveStream = null;
					try
					{
						if (!string.IsNullOrEmpty(currentPlayingPath))
						{
							try
							{
								waveStream = new AudioFileReader(currentPlayingPath);
							}
							catch
							{
								waveStream = new WaveFileReader(currentPlayingPath);
							}
							try
							{
								if (waveStream is AudioFileReader audioFileReader2)
								{
									audioFileReader2.CurrentTime = currentTime;
								}
								else
								{
									waveStream.Position = (long)(currentTime.TotalSeconds * (double)waveStream.WaveFormat.AverageBytesPerSecond);
								}
							}
							catch
							{
							}
						}
						else if (reader != null)
						{
							waveStream = reader;
						}
					}
					catch
					{
						waveStream = reader;
					}
					try
					{
						reader?.Dispose();
					}
					catch
					{
					}
					reader = waveStream;
					output = new WaveOutEvent();
					try
					{
						if (reader != null && Math.Abs(playbackRate - 1.0) > 0.0001)
						{
							try
							{
								SampleToWaveProvider16 waveProvider = new SampleToWaveProvider16(new VarispeedSampleProvider(reader.ToSampleProvider(), playbackRate));
								output.Init(waveProvider);
							}
							catch
							{
								output.Init(reader);
							}
						}
						else
						{
							output.Init(reader);
						}
					}
					catch
					{
						try
						{
							output.Init(reader);
						}
						catch
						{
						}
					}
					output.Volume = 1f;
					output.Play();
				}
				catch
				{
				}
			}
			try
			{
				if ((output != null && output.PlaybackState == PlaybackState.Playing) || string.IsNullOrEmpty(lastFmsPath) || lastTrackIndex < 0)
				{
					return;
				}
				try
				{
					Task.Run(delegate
					{
						PlayTrack(lastFmsPath, lastTrackIndex);
					});
				}
				catch
				{
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	public void WarmAndPrime(string fmsPath)
	{
		try
		{
			if (string.IsNullOrEmpty(fmsPath) || !File.Exists(fmsPath))
			{
				return;
			}
			try
			{
				if (!IsLoaded && !string.IsNullOrEmpty(famiFolder) && Directory.Exists(famiFolder))
				{
					LoadFromFolder(famiFolder);
				}
			}
			catch
			{
			}
			try
			{
				EnumerateTracks(fmsPath);
			}
			catch
			{
			}
			if (alc == null)
			{
				return;
			}
			foreach (Assembly assembly in alc.Assemblies)
			{
				Type type = assembly.GetTypes().FirstOrDefault((Type t) => t.Name.ToLower().Contains("player") || t.Name.ToLower().Contains("audio"));
				if (type == null)
				{
					continue;
				}
				List<MethodInfo> list = (from mi in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
					where mi.Name.ToLower().Contains("export") || mi.Name.ToLower().Contains("render") || mi.Name.ToLower().Contains("play")
					select mi).ToList();
				object obj3 = null;
				foreach (MethodInfo item in list)
				{
					try
					{
						ParameterInfo[] parameters = item.GetParameters();
						if (!item.IsStatic && obj3 == null)
						{
							obj3 = Activator.CreateInstance(type);
						}
						if (parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(int) && typeof(Stream).IsAssignableFrom(parameters[2].ParameterType))
						{
							using MemoryStream memoryStream = new MemoryStream();
							item.Invoke(obj3, new object[3] { fmsPath, 0, memoryStream });
							if (memoryStream.Length > 0)
							{
								try
								{
									memoryStream.Position = 0L;
									using WaveFileReader waveProvider = new WaveFileReader(memoryStream);
									using WaveOutEvent waveOutEvent = new WaveOutEvent();
									waveOutEvent.Init(waveProvider);
									return;
								}
								catch
								{
									return;
								}
							}
						}
						if (parameters.Length != 2 || !(parameters[0].ParameterType == typeof(string)) || !(parameters[1].ParameterType == typeof(int)) || !(item.ReturnType == typeof(byte[])) || !(item.Invoke(obj3, new object[2] { fmsPath, 0 }) is byte[] array) || array.Length == 0)
						{
							continue;
						}
						try
						{
							using MemoryStream inputStream = new MemoryStream(array);
							using WaveFileReader waveProvider2 = new WaveFileReader(inputStream);
							using WaveOutEvent waveOutEvent2 = new WaveOutEvent();
							waveOutEvent2.Init(waveProvider2);
							return;
						}
						catch
						{
							return;
						}
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}
}
