using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Microsoft.Win32;

namespace FamidashEditor;

public class FmsPlayerWindow : Window, IComponentConnector
{
	private readonly FamiStudioIntegration fami;

	private string? currentFmsPath;

	internal Button BtnOpen;

	internal TextBlock TxtPath;

	internal ComboBox ComboTracks;

	internal Button BtnPlay;

	internal Button BtnStop;

	internal TextBlock TxtStatus;

	private bool _contentLoaded;

	public FmsPlayerWindow()
	{
		InitializeComponent();
		fami = new FamiStudioIntegration();
		BtnOpen.Click += BtnOpen_Click;
		BtnPlay.Click += BtnPlay_Click;
		BtnStop.Click += BtnStop_Click;
		UpdateUiState();
		try
		{
			string path = "C:\\Editor Test\\the album.fms";
			if (File.Exists(path))
			{
				LoadFms(path);
			}
		}
		catch
		{
		}
	}

	private void UpdateUiState()
	{
		BtnPlay.IsEnabled = currentFmsPath != null && ComboTracks.Items.Count > 0;
		BtnStop.IsEnabled = fami.IsPlaying;
		TxtPath.Text = currentFmsPath ?? "No file selected";
		TxtStatus.Text = fami.StatusMessage ?? "";
	}

	private void BtnOpen_Click(object? sender, RoutedEventArgs e)
	{
		OpenFileDialog openFileDialog = new OpenFileDialog();
		openFileDialog.Filter = "FamiStudio files (*.fms)|*.fms|All files|*.*";
		if (openFileDialog.ShowDialog().GetValueOrDefault())
		{
			LoadFms(openFileDialog.FileName);
		}
	}

	private void LoadFms(string path)
	{
		currentFmsPath = path;
		TxtPath.Text = currentFmsPath;
		TxtStatus.Text = "Loading...";
		ComboTracks.Items.Clear();
		ComboTracks.Items.Add("Loading...");
		UpdateUiState();
		try
		{
			List<string> list = fami.TryParseFmsSongNames(currentFmsPath);
			if (list != null && list.Count > 0)
			{
				ComboTracks.Items.Clear();
				foreach (string item in list)
				{
					ComboTracks.Items.Add(item);
				}
				if (ComboTracks.Items.Count > 0)
				{
					ComboTracks.SelectedIndex = 0;
				}
				TxtStatus.Text = "Tracks parsed from .fms";
			}
		}
		catch
		{
		}
		Task.Run(delegate
		{
			try
			{
				if (!fami.IsLoaded)
				{
					try
					{
						string text = "C:\\Editor Test\\native-windows\\libs\\famistudio";
						if (Directory.Exists(text))
						{
							fami.LoadFromFolder(text);
						}
					}
					catch
					{
					}
					string text2 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", "famistudio");
					if (Directory.Exists(text2))
					{
						fami.LoadFromFolder(text2);
					}
					else
					{
						DirectoryInfo directoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
						bool flag = false;
						int num = 0;
						while (directoryInfo != null && num < 8 && !flag)
						{
							string text3 = Path.Combine(directoryInfo.FullName, "native-windows", "libs", "famistudio");
							if (Directory.Exists(text3))
							{
								try
								{
									fami.LoadFromFolder(text3);
									flag = fami.IsLoaded;
								}
								catch
								{
								}
								if (flag)
								{
									break;
								}
							}
							string text4 = Path.Combine(directoryInfo.FullName, "libs", "famistudio");
							if (Directory.Exists(text4))
							{
								try
								{
									fami.LoadFromFolder(text4);
									flag = fami.IsLoaded;
								}
								catch
								{
								}
								if (flag)
								{
									break;
								}
							}
							try
							{
								string[] files = Directory.GetFiles(directoryInfo.FullName, "FamiStudio.exe", SearchOption.TopDirectoryOnly);
								if (files.Length == 0)
								{
									string path2 = Path.Combine(directoryInfo.FullName, "famistudio");
									if (Directory.Exists(path2))
									{
										files = Directory.GetFiles(path2, "FamiStudio.exe", SearchOption.TopDirectoryOnly);
									}
								}
								if (files.Length != 0)
								{
									string directoryName = Path.GetDirectoryName(files[0]);
									try
									{
										fami.LoadFromFolder(directoryName);
										flag = fami.IsLoaded;
									}
									catch
									{
									}
									if (flag)
									{
										break;
									}
								}
							}
							catch
							{
							}
							directoryInfo = directoryInfo.Parent;
							num++;
						}
					}
				}
				List<string> discovered = new List<string>();
				if (fami.IsLoaded)
				{
					discovered = fami.EnumerateTracks(currentFmsPath);
				}
				if (discovered == null || discovered.Count == 0)
				{
					discovered = fami.ProbeTracksViaCli(currentFmsPath, 64);
				}
				base.Dispatcher.Invoke(delegate
				{
					ComboTracks.Items.Clear();
					if (discovered != null && discovered.Count > 0)
					{
						foreach (string item2 in discovered)
						{
							ComboTracks.Items.Add(item2);
						}
					}
					if (ComboTracks.Items.Count > 0)
					{
						ComboTracks.SelectedIndex = 0;
					}
					TxtStatus.Text = fami.StatusMessage ?? "Ready";
					UpdateUiState();
				});
			}
			catch (Exception ex2)
			{
				Exception ex3 = ex2;
				Exception ex = ex3;
				base.Dispatcher.Invoke(delegate
				{
					TxtStatus.Text = "In-process integration failed: " + ex.Message;
					UpdateUiState();
				});
			}
		});
	}

	private async void BtnPlay_Click(object? sender, RoutedEventArgs e)
	{
		if (currentFmsPath == null)
		{
			return;
		}
		int trackIndex = ComboTracks.SelectedIndex;
		if (trackIndex < 0)
		{
			trackIndex = 0;
		}
		TxtStatus.Text = "Playing...";
		UpdateUiState();
		try
		{
			await Task.Run(delegate
			{
				fami.PlayTrack(currentFmsPath, trackIndex);
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "Play failed: " + ex.Message);
		}
		TxtStatus.Text = fami.StatusMessage ?? "Ready";
		UpdateUiState();
	}

	private void BtnStop_Click(object? sender, RoutedEventArgs e)
	{
		fami.Stop();
		TxtStatus.Text = fami.StatusMessage ?? "Stopped";
		UpdateUiState();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/fmsplayerwindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			BtnOpen = (Button)target;
			break;
		case 2:
			TxtPath = (TextBlock)target;
			break;
		case 3:
			ComboTracks = (ComboBox)target;
			break;
		case 4:
			BtnPlay = (Button)target;
			break;
		case 5:
			BtnStop = (Button)target;
			break;
		case 6:
			TxtStatus = (TextBlock)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
