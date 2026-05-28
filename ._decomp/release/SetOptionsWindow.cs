using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;

namespace FamidashEditor;

public class SetOptionsWindow : Window, IComponentConnector
{
	private class LevelMetadata
	{
		public LevelData[]? official_levels { get; set; }

		public LevelData[]? community_levels { get; set; }
	}

	private class LevelData
	{
		public string? level { get; set; }

		public string? decoType { get; set; }

		public string? spikeSet { get; set; }

		public string? blockSet { get; set; }

		public string? sawSet { get; set; }

		public bool? parallaxDisable { get; set; }

		public ObjectOffsetEntry[]? objectOffsets { get; set; }

		public string? songID { get; set; }

		public string? difficulty { get; set; }

		public int? stars { get; set; }

		public string? lowerText { get; set; }

		public string? upperText { get; set; }

		public int? startingSpeed { get; set; }

		public int? startingBackgroundColor { get; set; }

		public int? startingGroundColor { get; set; }

		public int? startingGameMode { get; set; }

		public int? maxFallSpeed { get; set; }

		public int? spawnYPositionHi { get; set; }

		public int? spawnYPositionLow { get; set; }

		public int? scrollYPositionHi { get; set; }

		public int? scrollYPositionLow { get; set; }

		public bool? forcePlatformer { get; set; }
	}

	private bool _isInitializing = true;

	private string originalDeco = "DECO1";

	private string originalBlockSet = "BLOCKSA";

	private string originalSpikeSet = "SPIKESA";

	private bool originalLockSprites;

	private bool originalShowAccurateTileset;

	private bool originalNoParallax;

	internal ComboBox DecoCombo;

	internal CheckBox LockSpritesCheckBox;

	internal ComboBox BlockCombo;

	internal ComboBox StartingBackgroundColorCombo;

	internal ComboBox SpikeCombo;

	internal ComboBox StartingGroundColorCombo;

	internal ComboBox StartingGameModeCombo;

	internal CheckBox ShowAccurateTilesetCheckBox;

	internal ComboBox StartingSpeedCombo;

	internal ComboBox DifficultyCombo;

	internal ComboBox StarsCombo;

	internal CheckBox ForcePlatformerCheckBox;

	internal TextBox UpperTextBox;

	internal TextBox LowerTextBox;

	internal TextBox ScrollYPositionHiTextBox;

	internal TextBox ScrollYPositionLowTextBox;

	internal Button OkButton;

	internal Button CancelButton;

	internal Button BgTintButton;

	internal Button GroundTintButton;

	internal Button TileTintButton;

	internal CheckBox NoParallaxCheckBox;

	internal ComboBox MaxFallSpeedCombo;

	internal Button AttemptJsonLoadButton;

	internal Button RemoveSpriteShiftsButton;

	internal Button ExportShiftJsonButton;

	internal TextBox SpawnYPositionHiTextBox;

	internal TextBox SpawnYPositionLowTextBox;

	private bool _contentLoaded;

	public string SelectedDeco { get; private set; } = "DECO1";


	public string SelectedBlockSet { get; private set; } = "BLOCKSA";


	public string SelectedSpikeSet { get; private set; } = "SPIKESA";


	public bool LockSpritesToSet { get; private set; }

	public int SelectedStartingSpeedUiIndex { get; private set; } = 1;


	public int? SelectedStartingBackgroundColor { get; private set; }

	public int? SelectedStartingGroundColor { get; private set; }

	public SetOptionsWindow(string current, string currentBlock, string currentSpike)
	{
		InitializeComponent();
		originalDeco = current;
		originalBlockSet = currentBlock;
		originalSpikeSet = currentSpike;
		foreach (object item in (IEnumerable)DecoCombo.Items)
		{
			if (item is ComboBoxItem comboBoxItem && (string)comboBoxItem.Content == current)
			{
				DecoCombo.SelectedItem = item;
				break;
			}
		}
		foreach (object item2 in (IEnumerable)BlockCombo.Items)
		{
			if (item2 is ComboBoxItem comboBoxItem2 && (string)comboBoxItem2.Content == currentBlock)
			{
				BlockCombo.SelectedItem = item2;
				break;
			}
		}
		foreach (object item3 in (IEnumerable)SpikeCombo.Items)
		{
			if (item3 is ComboBoxItem comboBoxItem3 && (string)comboBoxItem3.Content == currentSpike)
			{
				SpikeCombo.SelectedItem = item3;
				break;
			}
		}
		OkButton.Click += delegate
		{
			if (DecoCombo.SelectedItem is ComboBoxItem comboBoxItem21)
			{
				SelectedDeco = (string)comboBoxItem21.Content;
			}
			if (BlockCombo.SelectedItem is ComboBoxItem comboBoxItem22)
			{
				SelectedBlockSet = (string)comboBoxItem22.Content;
			}
			if (SpikeCombo.SelectedItem is ComboBoxItem comboBoxItem23)
			{
				SelectedSpikeSet = (string)comboBoxItem23.Content;
			}
			if (base.Owner is MainWindow mainWindow11)
			{
				if (SelectedDeco != originalDeco)
				{
					mainWindow11.SetDecoSet(SelectedDeco);
				}
				if (SelectedBlockSet != originalBlockSet)
				{
					mainWindow11.SetBlockSet(SelectedBlockSet);
				}
				if (SelectedSpikeSet != originalSpikeSet)
				{
					mainWindow11.SetSpikeSet(SelectedSpikeSet);
				}
				CheckBox lockSpritesCheckBox = LockSpritesCheckBox;
				if (lockSpritesCheckBox != null && lockSpritesCheckBox.IsChecked.GetValueOrDefault())
				{
					mainWindow11.SetLockSpritesToSet(enabled: true, SelectedDeco);
				}
				else
				{
					CheckBox lockSpritesCheckBox2 = LockSpritesCheckBox;
					if (lockSpritesCheckBox2 != null && lockSpritesCheckBox2.IsChecked == false && originalLockSprites)
					{
						mainWindow11.SetLockSpritesToSet(enabled: false, SelectedDeco);
					}
				}
				if (ShowAccurateTilesetCheckBox?.IsChecked != originalShowAccurateTileset)
				{
					mainWindow11.SetShowAccurateTileset(ShowAccurateTilesetCheckBox?.IsChecked.GetValueOrDefault() ?? false, SelectedBlockSet, SelectedSpikeSet);
				}
				if (NoParallaxCheckBox?.IsChecked != originalNoParallax)
				{
					mainWindow11.SetNoParallax(NoParallaxCheckBox?.IsChecked.GetValueOrDefault() ?? false);
				}
				try
				{
					if (StartingSpeedCombo != null && StartingSpeedCombo.SelectedIndex >= 0)
					{
						SelectedStartingSpeedUiIndex = StartingSpeedCombo.SelectedIndex;
						mainWindow11.LoadedStartingSpeedUiIndex = SelectedStartingSpeedUiIndex;
					}
				}
				catch
				{
				}
				try
				{
					if (StartingBackgroundColorCombo != null && StartingBackgroundColorCombo.SelectedItem is ComboBoxItem { Content: string content3 })
					{
						try
						{
							mainWindow11.LoadedStartingBackgroundColor = int.Parse(content3.Replace("0x", ""), NumberStyles.HexNumber);
						}
						catch
						{
							mainWindow11.LoadedStartingBackgroundColor = null;
						}
					}
					if (StartingGroundColorCombo != null && StartingGroundColorCombo.SelectedItem is ComboBoxItem { Content: string content4 })
					{
						try
						{
							mainWindow11.LoadedStartingGroundColor = int.Parse(content4.Replace("0x", ""), NumberStyles.HexNumber);
						}
						catch
						{
							mainWindow11.LoadedStartingGroundColor = null;
						}
					}
				}
				catch
				{
				}
				try
				{
					if (SpawnYPositionHiTextBox != null && !string.IsNullOrWhiteSpace(SpawnYPositionHiTextBox.Text))
					{
						if (byte.TryParse(SpawnYPositionHiTextBox.Text.Trim().Replace("0x", "").Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result10))
						{
							mainWindow11.LoadedSpawnYPositionHi = result10;
						}
						else
						{
							mainWindow11.LoadedSpawnYPositionHi = null;
						}
					}
					else
					{
						mainWindow11.LoadedSpawnYPositionHi = null;
					}
					if (SpawnYPositionLowTextBox != null && !string.IsNullOrWhiteSpace(SpawnYPositionLowTextBox.Text))
					{
						if (byte.TryParse(SpawnYPositionLowTextBox.Text.Trim().Replace("0x", "").Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result11))
						{
							mainWindow11.LoadedSpawnYPositionLow = result11;
						}
						else
						{
							mainWindow11.LoadedSpawnYPositionLow = null;
						}
					}
					else
					{
						mainWindow11.LoadedSpawnYPositionLow = null;
					}
					if (ScrollYPositionHiTextBox != null && !string.IsNullOrWhiteSpace(ScrollYPositionHiTextBox.Text))
					{
						if (byte.TryParse(ScrollYPositionHiTextBox.Text.Trim().Replace("0x", "").Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result12))
						{
							mainWindow11.LoadedScrollYPositionHi = result12;
						}
						else
						{
							mainWindow11.LoadedScrollYPositionHi = null;
						}
					}
					else
					{
						mainWindow11.LoadedScrollYPositionHi = null;
					}
					if (ScrollYPositionLowTextBox != null && !string.IsNullOrWhiteSpace(ScrollYPositionLowTextBox.Text))
					{
						if (byte.TryParse(ScrollYPositionLowTextBox.Text.Trim().Replace("0x", "").Replace("0X", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result13))
						{
							mainWindow11.LoadedScrollYPositionLow = result13;
						}
						else
						{
							mainWindow11.LoadedScrollYPositionLow = null;
						}
					}
					else
					{
						mainWindow11.LoadedScrollYPositionLow = null;
					}
				}
				catch
				{
				}
				try
				{
					mainWindow11.PersistLoadedValuesToCurrentTab();
				}
				catch
				{
				}
				try
				{
					if (ForcePlatformerCheckBox != null)
					{
						mainWindow11.LoadedForcePlatformer = (ForcePlatformerCheckBox.IsChecked.GetValueOrDefault() ? new bool?(true) : new bool?(false));
					}
					else
					{
						mainWindow11.LoadedForcePlatformer = null;
					}
				}
				catch
				{
				}
				if (!_isInitializing)
				{
					mainWindow11.SaveCurrentTmxConfig();
				}
			}
			base.DialogResult = true;
		};
		CancelButton.Click += delegate
		{
			if (base.Owner is MainWindow mainWindow10)
			{
				if (DecoCombo.SelectedItem is ComboBoxItem comboBoxItem18 && (string)comboBoxItem18.Content != originalDeco)
				{
					mainWindow10.SetDecoSet(originalDeco);
				}
				if (BlockCombo.SelectedItem is ComboBoxItem comboBoxItem19 && (string)comboBoxItem19.Content != originalBlockSet)
				{
					mainWindow10.SetBlockSet(originalBlockSet);
				}
				if (SpikeCombo.SelectedItem is ComboBoxItem comboBoxItem20 && (string)comboBoxItem20.Content != originalSpikeSet)
				{
					mainWindow10.SetSpikeSet(originalSpikeSet);
				}
				if (LockSpritesCheckBox?.IsChecked != originalLockSprites)
				{
					mainWindow10.SetLockSpritesToSet(originalLockSprites, originalDeco);
				}
				if (ShowAccurateTilesetCheckBox?.IsChecked != originalShowAccurateTileset)
				{
					mainWindow10.SetShowAccurateTileset(originalShowAccurateTileset, originalBlockSet, originalSpikeSet);
				}
				if (NoParallaxCheckBox?.IsChecked != originalNoParallax)
				{
					mainWindow10.SetNoParallax(originalNoParallax);
				}
			}
			base.DialogResult = false;
		};
		BgTintButton.Click += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow9)
				{
					mainWindow9.ShowBackgroundTintPicker();
				}
			}
			catch
			{
			}
		};
		GroundTintButton.Click += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow8)
				{
					mainWindow8.ShowGroundTintPicker();
				}
			}
			catch
			{
			}
		};
		TileTintButton.Click += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow7)
				{
					mainWindow7.ShowTileTintPicker();
				}
			}
			catch
			{
			}
		};
		AttemptJsonLoadButton.Click += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow6)
				{
					AttemptJsonLoad(mainWindow6);
				}
			}
			catch (Exception ex3)
			{
				MessageBox.Show("Error loading JSON: " + ex3.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		};
		try
		{
			List<IInputElement> obj = new List<IInputElement>
			{
				DecoCombo, LockSpritesCheckBox, BlockCombo, SpikeCombo, ShowAccurateTilesetCheckBox, StarsCombo, StartingBackgroundColorCombo, StartingGroundColorCombo, StartingGameModeCombo, StartingSpeedCombo,
				DifficultyCombo, UpperTextBox, LowerTextBox, BgTintButton, GroundTintButton, TileTintButton, NoParallaxCheckBox, MaxFallSpeedCombo, AttemptJsonLoadButton, RemoveSpriteShiftsButton,
				ExportShiftJsonButton
			};
			int num = 0;
			foreach (IInputElement item4 in obj)
			{
				if (item4 == null)
				{
					continue;
				}
				try
				{
					if (item4 is Control control)
					{
						control.TabIndex = num;
						control.IsTabStop = true;
					}
					else if (item4 is UIElement uIElement)
					{
						uIElement.Focusable = true;
						KeyboardNavigation.SetTabIndex(uIElement, num);
					}
					num++;
				}
				catch
				{
				}
			}
			try
			{
				OkButton.TabIndex = num++;
				OkButton.IsTabStop = true;
			}
			catch
			{
			}
			try
			{
				CancelButton.TabIndex = num++;
				CancelButton.IsTabStop = true;
			}
			catch
			{
			}
		}
		catch
		{
		}
		RemoveSpriteShiftsButton.Click += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow5)
				{
					RemoveAllSpriteShifts(mainWindow5);
				}
			}
			catch (Exception ex2)
			{
				MessageBox.Show("Error removing sprite shifts: " + ex2.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		};
		ExportShiftJsonButton.Click += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow4)
				{
					ExportShiftJson(mainWindow4);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("Error exporting shift JSON: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
			}
		};
		try
		{
			base.PreviewKeyDown += delegate(object ss, KeyEventArgs ee)
			{
				try
				{
					if (ee.Key == Key.Escape)
					{
						try
						{
							CancelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.Click));
						}
						catch
						{
						}
						ee.Handled = true;
					}
					else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (ee.Key == Key.Return || ee.Key == Key.Return))
					{
						try
						{
							OkButton.RaiseEvent(new RoutedEventArgs(ButtonBase.Click));
						}
						catch
						{
						}
						ee.Handled = true;
					}
				}
				catch
				{
				}
			};
		}
		catch
		{
		}
		base.Loaded += delegate
		{
			try
			{
				if (base.Owner is MainWindow mainWindow && NoParallaxCheckBox != null)
				{
					originalNoParallax = mainWindow.NoParallaxBg;
					NoParallaxCheckBox.IsChecked = originalNoParallax;
				}
				if (base.Owner is MainWindow mainWindow2 && ShowAccurateTilesetCheckBox != null)
				{
					try
					{
						originalShowAccurateTileset = mainWindow2.ShowAccurateTileset;
					}
					catch
					{
						originalShowAccurateTileset = false;
					}
					ShowAccurateTilesetCheckBox.IsChecked = originalShowAccurateTileset;
				}
				if (base.Owner is MainWindow mainWindow3 && LockSpritesCheckBox != null)
				{
					try
					{
						originalLockSprites = mainWindow3.LockSpritesToSet;
					}
					catch
					{
						originalLockSprites = false;
					}
					LockSpritesCheckBox.IsChecked = originalLockSprites;
					LockSpritesToSet = originalLockSprites;
					LockSpritesCheckBox.Checked += delegate
					{
						LockSpritesToSet = true;
					};
					LockSpritesCheckBox.Unchecked += delegate
					{
						LockSpritesToSet = false;
					};
				}
				try
				{
					Window owner = base.Owner;
					MainWindow mwOwner = owner as MainWindow;
					if (mwOwner != null)
					{
						if (DecoCombo != null)
						{
							DecoCombo.SelectionChanged += delegate
							{
								try
								{
									if (DecoCombo.SelectedItem is ComboBoxItem comboBoxItem17)
									{
										string decoSet = (string)comboBoxItem17.Content;
										mwOwner.SetDecoSet(decoSet);
									}
								}
								catch
								{
								}
							};
						}
						if (BlockCombo != null)
						{
							BlockCombo.SelectionChanged += delegate
							{
								try
								{
									if (BlockCombo.SelectedItem is ComboBoxItem comboBoxItem16)
									{
										string blockSet = (string)comboBoxItem16.Content;
										mwOwner.SetBlockSet(blockSet);
									}
								}
								catch
								{
								}
							};
						}
						if (SpikeCombo != null)
						{
							SpikeCombo.SelectionChanged += delegate
							{
								try
								{
									if (SpikeCombo.SelectedItem is ComboBoxItem comboBoxItem15)
									{
										string spikeSet = (string)comboBoxItem15.Content;
										mwOwner.SetSpikeSet(spikeSet);
									}
								}
								catch
								{
								}
							};
						}
						if (NoParallaxCheckBox != null)
						{
							NoParallaxCheckBox.Checked += delegate
							{
								try
								{
									mwOwner.SetNoParallax(enabled: true);
								}
								catch
								{
								}
							};
							NoParallaxCheckBox.Unchecked += delegate
							{
								try
								{
									mwOwner.SetNoParallax(enabled: false);
								}
								catch
								{
								}
							};
						}
						try
						{
							if (MaxFallSpeedCombo != null)
							{
								try
								{
									MaxFallSpeedCombo.SelectedIndex = ((mwOwner.LoadedMaxFallSpeed == 7) ? 1 : 0);
								}
								catch
								{
									MaxFallSpeedCombo.SelectedIndex = 0;
								}
								MaxFallSpeedCombo.SelectionChanged += delegate
								{
									try
									{
										if (MaxFallSpeedCombo.SelectedItem is ComboBoxItem { Tag: not null } comboBoxItem14 && int.TryParse(comboBoxItem14.Tag.ToString(), out var result9))
										{
											mwOwner.LoadedMaxFallSpeed = result9;
											if (!_isInitializing)
											{
												try
												{
													mwOwner.SaveCurrentTmxConfig();
													return;
												}
												catch
												{
													return;
												}
											}
										}
									}
									catch
									{
									}
								};
							}
						}
						catch
						{
						}
						if (ShowAccurateTilesetCheckBox != null)
						{
							ShowAccurateTilesetCheckBox.Checked += delegate
							{
								try
								{
									mwOwner.SetShowAccurateTileset(enabled: true, (BlockCombo?.SelectedItem as ComboBoxItem)?.Content as string, (SpikeCombo?.SelectedItem as ComboBoxItem)?.Content as string);
								}
								catch
								{
								}
							};
							ShowAccurateTilesetCheckBox.Unchecked += delegate
							{
								try
								{
									mwOwner.SetShowAccurateTileset(enabled: false, (BlockCombo?.SelectedItem as ComboBoxItem)?.Content as string, (SpikeCombo?.SelectedItem as ComboBoxItem)?.Content as string);
								}
								catch
								{
								}
							};
						}
						(int?, int?, int?, int, int?, int?) loadedStartingValues = mwOwner.GetLoadedStartingValues();
						if (StartingSpeedCombo != null)
						{
							try
							{
								StartingSpeedCombo.SelectedIndex = loadedStartingValues.Item4;
							}
							catch
							{
							}
							StartingSpeedCombo.SelectionChanged += delegate
							{
								try
								{
									if (StartingSpeedCombo.SelectedIndex >= 0)
									{
										mwOwner.LoadedStartingSpeedUiIndex = StartingSpeedCombo.SelectedIndex;
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
								}
								catch
								{
								}
							};
						}
						if (StartingGameModeCombo != null)
						{
							try
							{
								if (loadedStartingValues.Item3.HasValue)
								{
									int value = loadedStartingValues.Item3.Value;
									for (int i = 0; i < StartingGameModeCombo.Items.Count; i++)
									{
										if (StartingGameModeCombo.Items[i] is ComboBoxItem { Tag: not null } comboBoxItem4 && int.TryParse(comboBoxItem4.Tag.ToString(), out var result) && result == value)
										{
											StartingGameModeCombo.SelectedIndex = i;
											break;
										}
									}
								}
							}
							catch
							{
							}
							StartingGameModeCombo.SelectionChanged += delegate
							{
								try
								{
									if (StartingGameModeCombo.SelectedItem is ComboBoxItem { Tag: not null } comboBoxItem13 && int.TryParse(comboBoxItem13.Tag.ToString(), out var result8))
									{
										mwOwner.LoadedStartingGameMode = result8;
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
								}
								catch
								{
								}
							};
						}
						if (StartingBackgroundColorCombo != null)
						{
							try
							{
								if (loadedStartingValues.Item1.HasValue)
								{
									string text = $"0x{loadedStartingValues.Item1.Value:X2}";
									for (int j = 0; j < StartingBackgroundColorCombo.Items.Count; j++)
									{
										if (StartingBackgroundColorCombo.Items[j] is ComboBoxItem comboBoxItem5 && (string)comboBoxItem5.Content == text)
										{
											StartingBackgroundColorCombo.SelectedIndex = j;
											break;
										}
									}
								}
							}
							catch
							{
							}
							StartingBackgroundColorCombo.SelectionChanged += delegate
							{
								try
								{
									if (StartingBackgroundColorCombo.SelectedItem is ComboBoxItem { Content: string content2 })
									{
										try
										{
											mwOwner.LoadedStartingBackgroundColor = int.Parse(content2.Replace("0x", ""), NumberStyles.HexNumber);
										}
										catch
										{
											mwOwner.LoadedStartingBackgroundColor = null;
										}
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
								}
								catch
								{
								}
							};
						}
						if (StartingGroundColorCombo != null)
						{
							try
							{
								if (loadedStartingValues.Item2.HasValue)
								{
									string text2 = $"0x{loadedStartingValues.Item2.Value:X2}";
									for (int k = 0; k < StartingGroundColorCombo.Items.Count; k++)
									{
										if (StartingGroundColorCombo.Items[k] is ComboBoxItem comboBoxItem6 && (string)comboBoxItem6.Content == text2)
										{
											StartingGroundColorCombo.SelectedIndex = k;
											break;
										}
									}
								}
							}
							catch
							{
							}
							StartingGroundColorCombo.SelectionChanged += delegate
							{
								try
								{
									if (StartingGroundColorCombo.SelectedItem is ComboBoxItem { Content: string content })
									{
										try
										{
											mwOwner.LoadedStartingGroundColor = int.Parse(content.Replace("0x", ""), NumberStyles.HexNumber);
										}
										catch
										{
											mwOwner.LoadedStartingGroundColor = null;
										}
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
								}
								catch
								{
								}
							};
						}
						if (DifficultyCombo != null)
						{
							try
							{
								if (loadedStartingValues.Item5.HasValue)
								{
									int value2 = loadedStartingValues.Item5.Value;
									for (int l = 0; l < DifficultyCombo.Items.Count; l++)
									{
										if (DifficultyCombo.Items[l] is ComboBoxItem { Tag: not null } comboBoxItem7 && int.TryParse(comboBoxItem7.Tag.ToString(), out var result2) && result2 == value2)
										{
											DifficultyCombo.SelectedIndex = l;
											break;
										}
									}
								}
							}
							catch
							{
							}
							DifficultyCombo.SelectionChanged += delegate
							{
								try
								{
									if (DifficultyCombo.SelectedItem is ComboBoxItem { Tag: not null } comboBoxItem10 && int.TryParse(comboBoxItem10.Tag.ToString(), out var result7))
									{
										mwOwner.LoadedStartingDifficulty = result7;
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
								}
								catch
								{
								}
							};
						}
						if (StarsCombo != null)
						{
							try
							{
								if (loadedStartingValues.Item6.HasValue)
								{
									int value3 = loadedStartingValues.Item6.Value;
									for (int m = 0; m < StarsCombo.Items.Count; m++)
									{
										if (StarsCombo.Items[m] is ComboBoxItem { Tag: not null } comboBoxItem8 && int.TryParse(comboBoxItem8.Tag.ToString(), out var result3) && result3 == value3)
										{
											StarsCombo.SelectedIndex = m;
											break;
										}
									}
								}
							}
							catch
							{
							}
							StarsCombo.SelectionChanged += delegate
							{
								try
								{
									if (StarsCombo.SelectedItem is ComboBoxItem { Tag: not null } comboBoxItem9 && int.TryParse(comboBoxItem9.Tag.ToString(), out var result6))
									{
										mwOwner.LoadedStartingStars = result6;
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
								}
								catch
								{
								}
							};
						}
						try
						{
							try
							{
								if (LowerTextBox != null)
								{
									LowerTextBox.Text = mwOwner.LoadedStartingLowerText ?? "";
								}
							}
							catch
							{
							}
							try
							{
								if (UpperTextBox != null)
								{
									UpperTextBox.Text = mwOwner.LoadedStartingUpperText ?? "";
								}
							}
							catch
							{
							}
							try
							{
								if (UpperTextBox != null)
								{
									UpperTextBox.IsEnabled = !string.IsNullOrEmpty(mwOwner.LoadedStartingLowerText) || !string.IsNullOrEmpty(mwOwner.LoadedStartingUpperText);
								}
							}
							catch
							{
							}
							if (LowerTextBox != null)
							{
								LowerTextBox.TextChanged += delegate
								{
									try
									{
										string text5 = LowerTextBox.Text ?? "";
										text5 = text5.ToUpperInvariant();
										if (LowerTextBox.Text != text5)
										{
											LowerTextBox.Text = text5;
											LowerTextBox.CaretIndex = text5.Length;
										}
										mwOwner.LoadedStartingLowerText = (string.IsNullOrEmpty(text5) ? null : text5);
										try
										{
											if (UpperTextBox != null)
											{
												if (string.IsNullOrEmpty(text5))
												{
													UpperTextBox.Text = "";
													UpperTextBox.IsEnabled = false;
													mwOwner.LoadedStartingUpperText = null;
												}
												else
												{
													UpperTextBox.IsEnabled = true;
												}
											}
										}
										catch
										{
										}
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
									catch
									{
									}
								};
							}
							if (UpperTextBox != null)
							{
								UpperTextBox.TextChanged += delegate
								{
									try
									{
										string text4 = UpperTextBox.Text ?? "";
										text4 = text4.ToUpperInvariant();
										if (UpperTextBox.Text != text4)
										{
											UpperTextBox.Text = text4;
											UpperTextBox.CaretIndex = text4.Length;
										}
										mwOwner.LoadedStartingUpperText = (string.IsNullOrEmpty(text4) ? null : text4);
										if (!_isInitializing)
										{
											try
											{
												mwOwner.SaveCurrentTmxConfig();
												return;
											}
											catch
											{
												return;
											}
										}
									}
									catch
									{
									}
								};
							}
						}
						catch
						{
						}
						try
						{
							if (SpawnYPositionHiTextBox != null)
							{
								SpawnYPositionHiTextBox.Text = (mwOwner.LoadedSpawnYPositionHi.HasValue ? $"0x{mwOwner.LoadedSpawnYPositionHi.Value:X2}" : "");
							}
							if (SpawnYPositionLowTextBox != null)
							{
								SpawnYPositionLowTextBox.Text = (mwOwner.LoadedSpawnYPositionLow.HasValue ? $"0x{mwOwner.LoadedSpawnYPositionLow.Value:X2}" : "");
							}
							if (ScrollYPositionHiTextBox != null)
							{
								ScrollYPositionHiTextBox.Text = (mwOwner.LoadedScrollYPositionHi.HasValue ? $"0x{mwOwner.LoadedScrollYPositionHi.Value:X2}" : "");
							}
							if (ScrollYPositionLowTextBox != null)
							{
								ScrollYPositionLowTextBox.Text = (mwOwner.LoadedScrollYPositionLow.HasValue ? $"0x{mwOwner.LoadedScrollYPositionLow.Value:X2}" : "");
							}
							if (SpawnYPositionHiTextBox != null)
							{
								SpawnYPositionHiTextBox.LostFocus += FormatHexOnLost;
							}
							if (SpawnYPositionLowTextBox != null)
							{
								SpawnYPositionLowTextBox.LostFocus += FormatHexOnLost;
							}
							if (ScrollYPositionHiTextBox != null)
							{
								ScrollYPositionHiTextBox.LostFocus += FormatHexOnLost;
							}
							if (ScrollYPositionLowTextBox != null)
							{
								ScrollYPositionLowTextBox.LostFocus += FormatHexOnLost;
							}
							static void FormatHexOnLost(object? s, RoutedEventArgs ea)
							{
								try
								{
									if (s is TextBox textBox)
									{
										string text3 = textBox.Text ?? "";
										text3 = text3.Trim();
										if (text3.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
										{
											text3 = text3.Substring(2);
										}
										int result5;
										if (byte.TryParse(text3, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result4))
										{
											textBox.Text = $"0x{result4:X2}";
										}
										else if (int.TryParse(text3, out result5) && result5 >= 0 && result5 <= 255)
										{
											textBox.Text = $"0x{result5:X2}";
										}
										else if (string.IsNullOrWhiteSpace(text3))
										{
											textBox.Text = "";
										}
										else
										{
											textBox.Text = text3.ToUpperInvariant();
										}
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
						try
						{
							if (ForcePlatformerCheckBox != null)
							{
								ForcePlatformerCheckBox.IsChecked = mwOwner.LoadedForcePlatformer.GetValueOrDefault();
							}
						}
						catch
						{
						}
						_isInitializing = false;
					}
				}
				catch
				{
				}
			}
			catch
			{
			}
		};
	}

	private void AttemptJsonLoad(MainWindow mainWindow)
	{
		try
		{
			string text = Path.Combine(AppContext.BaseDirectory, "lvlset_HUGE_metadata.json5");
			if (!File.Exists(text))
			{
				MessageBox.Show("JSON metadata file not found at: " + text, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
			string json = File.ReadAllText(text);
			string text2;
			try
			{
				text2 = ConvertJson5ToJson(json);
			}
			catch (Exception ex)
			{
				MessageBox.Show("Failed to convert JSON5: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
			LevelMetadata levelMetadata;
			try
			{
				JsonSerializerOptions options = new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true,
					AllowTrailingCommas = true,
					ReadCommentHandling = JsonCommentHandling.Skip
				};
				levelMetadata = JsonSerializer.Deserialize<LevelMetadata>(text2, options);
			}
			catch (Exception ex2)
			{
				string text3 = Path.Combine(Path.GetTempPath(), "converted_json_debug.json");
				File.WriteAllText(text3, text2);
				MessageBox.Show("JSON parse error: " + ex2.Message + "\n\nConverted JSON saved to: " + text3, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
			if (levelMetadata == null)
			{
				MessageBox.Show("Invalid JSON format - metadata is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
			string currentTmxPath = mainWindow.GetCurrentTmxPath();
			if (string.IsNullOrEmpty(currentTmxPath))
			{
				MessageBox.Show("No TMX file is currently loaded.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
			string levelName = Path.GetFileNameWithoutExtension(currentTmxPath).ToLower();
			LevelData levelData = null;
			if (levelMetadata.official_levels != null)
			{
				levelData = levelMetadata.official_levels.FirstOrDefault((LevelData l) => l.level?.ToLower() == levelName);
			}
			if (levelData == null && levelMetadata.community_levels != null)
			{
				levelData = levelMetadata.community_levels.FirstOrDefault((LevelData l) => l.level?.ToLower() == levelName);
			}
			if (levelData == null)
			{
				MessageBox.Show("Level not found in JSON.", "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
				return;
			}
			bool flag = false;
			if (!string.IsNullOrEmpty(levelData.decoType))
			{
				mainWindow.SetDecoSet(levelData.decoType);
				foreach (object item in (IEnumerable)DecoCombo.Items)
				{
					if (item is ComboBoxItem comboBoxItem && (string)comboBoxItem.Content == levelData.decoType)
					{
						DecoCombo.SelectedItem = item;
						break;
					}
				}
				SelectedDeco = levelData.decoType;
				flag = true;
			}
			if (!string.IsNullOrEmpty(levelData.blockSet))
			{
				string text4 = "BLOCKS" + levelData.blockSet;
				mainWindow.SetBlockSet(text4);
				foreach (object item2 in (IEnumerable)BlockCombo.Items)
				{
					if (item2 is ComboBoxItem comboBoxItem2 && (string)comboBoxItem2.Content == text4)
					{
						BlockCombo.SelectedItem = item2;
						break;
					}
				}
				SelectedBlockSet = text4;
				flag = true;
			}
			if (!string.IsNullOrEmpty(levelData.spikeSet))
			{
				string text5 = "SPIKES" + levelData.spikeSet;
				mainWindow.SetSpikeSet(text5);
				foreach (object item3 in (IEnumerable)SpikeCombo.Items)
				{
					if (item3 is ComboBoxItem comboBoxItem3 && (string)comboBoxItem3.Content == text5)
					{
						SpikeCombo.SelectedItem = item3;
						break;
					}
				}
				SelectedSpikeSet = text5;
				flag = true;
			}
			if (levelData.parallaxDisable.HasValue)
			{
				mainWindow.SetNoParallax(levelData.parallaxDisable.Value);
				if (NoParallaxCheckBox != null)
				{
					NoParallaxCheckBox.IsChecked = levelData.parallaxDisable.Value;
				}
				flag = true;
			}
			if (levelData.objectOffsets != null && levelData.objectOffsets.Length != 0)
			{
				mainWindow.ApplySpriteOffsets(levelData.objectOffsets);
				flag = true;
			}
			if (!string.IsNullOrEmpty(levelData.songID))
			{
				mainWindow.SetSongFromMetadata(levelData.songID);
				try
				{
					object obj = null;
					PropertyInfo property = mainWindow.GetType().GetProperty("FamiTrackCombo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (property != null)
					{
						obj = property.GetValue(mainWindow);
					}
					else
					{
						FieldInfo field = mainWindow.GetType().GetField("FamiTrackCombo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (field != null)
						{
							obj = field.GetValue(mainWindow);
						}
					}
					if (obj != null)
					{
						object obj2 = obj.GetType().GetProperty("SelectedItem")?.GetValue(obj);
						if (obj2 != null)
						{
							PropertyInfo property2 = obj2.GetType().GetProperty("Content");
							string text6 = null;
							text6 = ((!(property2 != null)) ? obj2.ToString() : property2.GetValue(obj2)?.ToString());
							if (!string.IsNullOrEmpty(text6))
							{
								PropertyInfo property3 = mainWindow.GetType().GetProperty("SelectedSong", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
								if (property3 != null && property3.CanWrite)
								{
									property3.SetValue(mainWindow, text6);
								}
							}
						}
					}
				}
				catch
				{
				}
				flag = true;
			}
			if (levelData.startingSpeed.HasValue)
			{
				try
				{
					int value = levelData.startingSpeed.Value;
					int selectedIndex = (mainWindow.LoadedStartingSpeedUiIndex = value switch
					{
						0 => 1, 
						1 => 0, 
						_ => value, 
					});
					if (StartingSpeedCombo != null)
					{
						StartingSpeedCombo.SelectedIndex = selectedIndex;
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.maxFallSpeed.HasValue)
			{
				try
				{
					int num2 = levelData.maxFallSpeed.Value;
					if (num2 != 6 && num2 != 7)
					{
						num2 = 6;
					}
					mainWindow.LoadedMaxFallSpeed = num2;
					flag = true;
				}
				catch
				{
					mainWindow.LoadedMaxFallSpeed = 6;
				}
			}
			else
			{
				try
				{
					mainWindow.LoadedMaxFallSpeed = 6;
				}
				catch
				{
				}
			}
			try
			{
				if (MaxFallSpeedCombo != null)
				{
					MaxFallSpeedCombo.SelectedIndex = ((mainWindow.LoadedMaxFallSpeed == 7) ? 1 : 0);
				}
			}
			catch
			{
			}
			if (levelData.startingBackgroundColor.HasValue)
			{
				try
				{
					int value2 = levelData.startingBackgroundColor.Value;
					mainWindow.LoadedStartingBackgroundColor = value2;
					try
					{
						string text7 = $"0x{value2:X2}";
						if (StartingBackgroundColorCombo != null)
						{
							for (int i = 0; i < StartingBackgroundColorCombo.Items.Count; i++)
							{
								if (StartingBackgroundColorCombo.Items[i] is ComboBoxItem comboBoxItem4 && (string)comboBoxItem4.Content == text7)
								{
									StartingBackgroundColorCombo.SelectedIndex = i;
									break;
								}
							}
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.startingGroundColor.HasValue)
			{
				try
				{
					int value3 = levelData.startingGroundColor.Value;
					mainWindow.LoadedStartingGroundColor = value3;
					try
					{
						string text8 = $"0x{value3:X2}";
						if (StartingGroundColorCombo != null)
						{
							for (int j = 0; j < StartingGroundColorCombo.Items.Count; j++)
							{
								if (StartingGroundColorCombo.Items[j] is ComboBoxItem comboBoxItem5 && (string)comboBoxItem5.Content == text8)
								{
									StartingGroundColorCombo.SelectedIndex = j;
									break;
								}
							}
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.startingGameMode.HasValue)
			{
				try
				{
					int value4 = levelData.startingGameMode.Value;
					mainWindow.LoadedStartingGameMode = value4;
					if (StartingGameModeCombo != null)
					{
						for (int k = 0; k < StartingGameModeCombo.Items.Count; k++)
						{
							if (StartingGameModeCombo.Items[k] is ComboBoxItem { Tag: not null } comboBoxItem6 && int.TryParse(comboBoxItem6.Tag.ToString(), out var result) && result == value4)
							{
								StartingGameModeCombo.SelectedIndex = k;
								break;
							}
						}
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (!string.IsNullOrEmpty(levelData.difficulty))
			{
				try
				{
					string text9 = levelData.difficulty.Trim().ToUpperInvariant();
					int num3 = 6;
					num3 = text9 switch
					{
						"AUTO" => 6, 
						"EASY" => 0, 
						"NORMAL" => 1, 
						"HARD" => 2, 
						"HARDER" => 3, 
						"INSANE" => 4, 
						"DEMON" => 5, 
						"EASYDEMON" => 7, 
						"MEDIUMDEMON" => 8, 
						"HARDDEMON" => 9, 
						"INSANEDEMON" => 10, 
						"EXTREMEDEMON" => 11, 
						"IMPOSSIBLEDEMON" => 12, 
						"GRANDPADEMON" => 13, 
						_ => 6, 
					};
					mainWindow.LoadedStartingDifficulty = num3;
					try
					{
						if (DifficultyCombo != null)
						{
							for (int m = 0; m < DifficultyCombo.Items.Count; m++)
							{
								if (DifficultyCombo.Items[m] is ComboBoxItem { Tag: not null } comboBoxItem7 && int.TryParse(comboBoxItem7.Tag.ToString(), out var result2) && result2 == num3)
								{
									DifficultyCombo.SelectedIndex = m;
									break;
								}
							}
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.stars.HasValue)
			{
				try
				{
					int num4 = levelData.stars.Value;
					if (num4 < 1)
					{
						num4 = 1;
					}
					if (num4 > 15)
					{
						num4 = 15;
					}
					mainWindow.LoadedStartingStars = num4;
					try
					{
						if (StarsCombo != null)
						{
							for (int n = 0; n < StarsCombo.Items.Count; n++)
							{
								if (StarsCombo.Items[n] is ComboBoxItem { Tag: not null } comboBoxItem8 && int.TryParse(comboBoxItem8.Tag.ToString(), out var result3) && result3 == num4)
								{
									StarsCombo.SelectedIndex = n;
									break;
								}
							}
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (!string.IsNullOrEmpty(levelData.lowerText))
			{
				try
				{
					string text11 = (mainWindow.LoadedStartingLowerText = levelData.lowerText.Trim().ToUpperInvariant());
					try
					{
						if (LowerTextBox != null)
						{
							LowerTextBox.Text = text11;
						}
					}
					catch
					{
					}
					try
					{
						if (UpperTextBox != null)
						{
							UpperTextBox.IsEnabled = true;
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (!string.IsNullOrEmpty(levelData.upperText))
			{
				try
				{
					string text13 = (mainWindow.LoadedStartingUpperText = levelData.upperText.Trim().ToUpperInvariant());
					try
					{
						if (UpperTextBox != null)
						{
							UpperTextBox.Text = text13;
							UpperTextBox.IsEnabled = true;
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.spawnYPositionHi.HasValue)
			{
				try
				{
					int value5 = levelData.spawnYPositionHi.Value;
					mainWindow.LoadedSpawnYPositionHi = value5;
					try
					{
						if (SpawnYPositionHiTextBox != null)
						{
							SpawnYPositionHiTextBox.Text = $"0x{value5:X2}";
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.spawnYPositionLow.HasValue)
			{
				try
				{
					int value6 = levelData.spawnYPositionLow.Value;
					mainWindow.LoadedSpawnYPositionLow = value6;
					try
					{
						if (SpawnYPositionLowTextBox != null)
						{
							SpawnYPositionLowTextBox.Text = $"0x{value6:X2}";
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.scrollYPositionHi.HasValue)
			{
				try
				{
					int value7 = levelData.scrollYPositionHi.Value;
					mainWindow.LoadedScrollYPositionHi = value7;
					try
					{
						if (ScrollYPositionHiTextBox != null)
						{
							ScrollYPositionHiTextBox.Text = $"0x{value7:X2}";
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.scrollYPositionLow.HasValue)
			{
				try
				{
					int value8 = levelData.scrollYPositionLow.Value;
					mainWindow.LoadedScrollYPositionLow = value8;
					try
					{
						if (ScrollYPositionLowTextBox != null)
						{
							ScrollYPositionLowTextBox.Text = $"0x{value8:X2}";
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (levelData.forcePlatformer.HasValue)
			{
				try
				{
					bool value9 = levelData.forcePlatformer.Value;
					mainWindow.LoadedForcePlatformer = value9;
					try
					{
						if (ForcePlatformerCheckBox != null)
						{
							ForcePlatformerCheckBox.IsChecked = value9;
						}
					}
					catch
					{
					}
					flag = true;
				}
				catch
				{
				}
			}
			if (flag)
			{
				try
				{
					mainWindow.PersistLoadedValuesToCurrentTab();
				}
				catch
				{
				}
				mainWindow.SaveCurrentTmxConfig();
				MessageBox.Show("Settings loaded successfully from JSON and saved to config.", "Success", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception ex3)
		{
			MessageBox.Show("Error loading JSON: " + ex3.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void RemoveAllSpriteShifts(MainWindow mainWindow)
	{
		try
		{
			int spriteOffsetCount = mainWindow.GetSpriteOffsetCount();
			if (spriteOffsetCount == 0)
			{
				MessageBox.Show("No sprite shifts found on this map.", "Information", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
			else if (MessageBox.Show($"Are you sure you want to remove all {spriteOffsetCount} sprite shift(s) from this map?\n\nThis action cannot be undone.", "Confirm Remove Sprite Shifts", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
			{
				mainWindow.RemoveAllSpriteOffsets();
				MessageBox.Show($"Successfully removed {spriteOffsetCount} sprite shift(s).", "Success", MessageBoxButton.OK, MessageBoxImage.Asterisk);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("Error removing sprite shifts: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	private void ExportShiftJson(MainWindow mainWindow)
	{
		MainWindow mainWindow2 = mainWindow;
		try
		{
			Dictionary<int, (int offsetX, int offsetY)> spriteOffsets = mainWindow2.GetSpriteOffsets();
			int mapWidth = mainWindow2.MapWidth;
			_ = mainWindow2.MapHeight;
			Dictionary<(int, int), List<(int, int)>> dictionary = new Dictionary<(int, int), List<(int, int)>>();
			foreach (KeyValuePair<int, (int, int)> item3 in spriteOffsets)
			{
				int key = item3.Key;
				int item = key % mapWidth;
				int item2 = key / mapWidth;
				(int, int) value = item3.Value;
				(int, int) key2 = (value.Item1, value.Item2);
				if (!dictionary.ContainsKey(key2))
				{
					dictionary[key2] = new List<(int, int)>();
				}
				dictionary[key2].Add((item, item2));
			}
			StringBuilder stringBuilder = new StringBuilder();
			string currentTmxPath = mainWindow2.GetCurrentTmxPath();
			string text = (string.IsNullOrEmpty(currentTmxPath) ? "level" : Path.GetFileNameWithoutExtension(currentTmxPath).ToLowerInvariant());
			string value2 = GetString("LoadedStartingUpperText").ToUpperInvariant();
			string value3 = GetString("LoadedStartingLowerText").ToUpperInvariant();
			string value4 = GetString("LoadedDecoSet", (TryGetCurrentTabValue("LoadedDecoSet") as string) ?? "DECO1");
			string text2 = GetString("LoadedBlockSet", (TryGetCurrentTabValue("LoadedBlockSet") as string) ?? "BLOCKSA");
			string text3 = GetString("LoadedSpikeSet", (TryGetCurrentTabValue("LoadedSpikeSet") as string) ?? "SPIKESA");
			string text4 = ((text2 != null && text2.StartsWith("BLOCKS", StringComparison.OrdinalIgnoreCase)) ? text2.Substring(6) : (text2 ?? "A"));
			string text5 = ((text3 != null && text3.StartsWith("SPIKES", StringComparison.OrdinalIgnoreCase)) ? text3.Substring(6) : (text3 ?? "A"));
			text4 = (text4 ?? "").ToUpperInvariant();
			text5 = (text5 ?? "").ToUpperInvariant();
			string value5 = "AUTO";
			int? num = GetNullableInt("LoadedStartingDifficulty");
			try
			{
				if (num.HasValue)
				{
					int num2 = Math.Clamp(num.Value, 0, 13);
					string[] array = new string[7] { "EASY", "NORMAL", "HARD", "HARDER", "INSANE", "DEMON", "AUTO" };
					string[] array2 = new string[7] { "EASYDEMON", "MEDIUMDEMON", "HARDDEMON", "INSANEDEMON", "EXTREMEDEMON", "IMPOSSIBLEDEMON", "GRANDPADEMON" };
					value5 = ((num2 < 7) ? array[num2] : array2[num2 - 7]);
				}
			}
			catch
			{
				value5 = "AUTO";
			}
			int valueOrDefault = GetNullableInt("LoadedStartingStars").GetValueOrDefault(3);
			string text6 = GetString("SelectedSong");
			if (string.IsNullOrEmpty(text6))
			{
				text6 = (TryGetCurrentTabValue("SelectedSong") as string) ?? "";
			}
			if (string.IsNullOrEmpty(text6))
			{
				try
				{
					object obj2 = null;
					try
					{
						PropertyInfo property = mainWindow2.GetType().GetProperty("FamiTrackCombo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
						if (property != null)
						{
							obj2 = property.GetValue(mainWindow2);
						}
						else
						{
							FieldInfo field = mainWindow2.GetType().GetField("FamiTrackCombo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (field != null)
							{
								obj2 = field.GetValue(mainWindow2);
							}
						}
					}
					catch
					{
						obj2 = null;
					}
					if (obj2 != null)
					{
						object obj4 = obj2.GetType().GetProperty("SelectedItem")?.GetValue(obj2);
						if (obj4 != null)
						{
							PropertyInfo property2 = obj4.GetType().GetProperty("Content");
							if (property2 != null)
							{
								string text7 = property2.GetValue(obj4)?.ToString();
								if (!string.IsNullOrEmpty(text7))
								{
									text6 = text7;
								}
							}
							else
							{
								string text8 = obj4.ToString();
								if (!string.IsNullOrEmpty(text8))
								{
									text6 = text8;
								}
							}
						}
					}
				}
				catch
				{
				}
			}
			string value6 = "";
			if (!string.IsNullOrEmpty(text6))
			{
				string input = text6.ToLowerInvariant();
				input = Regex.Replace(input, "\\s+", "_");
				input = Regex.Replace(input, "[^a-z0-9_]", "");
				value6 = "song_" + input;
			}
			int valueOrDefault2 = GetNullableInt("LoadedStartingGameMode").GetValueOrDefault();
			int num3 = 0;
			try
			{
				int valueOrDefault3 = GetNullableInt("LoadedStartingSpeedUiIndex").GetValueOrDefault(1);
				num3 = valueOrDefault3 switch
				{
					1 => 0, 
					0 => 1, 
					_ => valueOrDefault3, 
				};
			}
			catch
			{
				num3 = 0;
			}
			int num4 = 6;
			try
			{
				int? num5 = GetNullableInt("LoadedMaxFallSpeed");
				if (num5.HasValue)
				{
					num4 = num5.Value;
				}
				else if (TryGetCurrentTabValue("LoadedMaxFallSpeed") is int num6)
				{
					num4 = num6;
				}
			}
			catch
			{
				try
				{
					if (TryGetCurrentTabValue("LoadedMaxFallSpeed") is int num7)
					{
						num4 = num7;
					}
				}
				catch
				{
				}
			}
			if (num4 == 0)
			{
				num4 = 6;
			}
			int? num8 = GetNullableInt("LoadedStartingBackgroundColor");
			int? num9 = GetNullableInt("LoadedStartingGroundColor");
			stringBuilder.AppendLine("\t\t{ ");
			StringBuilder stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder3 = stringBuilder2;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(13, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tlevel: \"");
			handler.AppendFormatted(text);
			handler.AppendLiteral("\",");
			stringBuilder3.AppendLine(ref handler);
			if (!string.IsNullOrEmpty(value2))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder4 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tupperText: \"");
				handler.AppendFormatted(value2);
				handler.AppendLiteral("\",");
				stringBuilder4.AppendLine(ref handler);
			}
			if (!string.IsNullOrEmpty(value3))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder5 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(17, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tlowerText: \"");
				handler.AppendFormatted(value3);
				handler.AppendLiteral("\",");
				stringBuilder5.AppendLine(ref handler);
			}
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder6 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tdecoType: \"");
			handler.AppendFormatted(value4);
			handler.AppendLiteral("\",");
			stringBuilder6.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder7 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tspikeSet: \"");
			handler.AppendFormatted(text5);
			handler.AppendLiteral("\",");
			stringBuilder7.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder8 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(16, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tblockSet: \"");
			handler.AppendFormatted(text4);
			handler.AppendLiteral("\",");
			stringBuilder8.AppendLine(ref handler);
			stringBuilder.AppendLine("\t\t\tsawSet: \"A\",");
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder9 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(18, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tdifficulty: \"");
			handler.AppendFormatted(value5);
			handler.AppendLiteral("\",");
			stringBuilder9.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder10 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(11, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tstars: ");
			handler.AppendFormatted(valueOrDefault);
			handler.AppendLiteral(",");
			stringBuilder10.AppendLine(ref handler);
			if (!string.IsNullOrEmpty(value6))
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder11 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tsongID: \"");
				handler.AppendFormatted(value6);
				handler.AppendLiteral("\",");
				stringBuilder11.AppendLine(ref handler);
			}
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder12 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(22, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tstartingGameMode: ");
			handler.AppendFormatted(valueOrDefault2);
			handler.AppendLiteral(",");
			stringBuilder12.AppendLine(ref handler);
			stringBuilder2 = stringBuilder;
			StringBuilder stringBuilder13 = stringBuilder2;
			handler = new StringBuilder.AppendInterpolatedStringHandler(19, 1, stringBuilder2);
			handler.AppendLiteral("\t\t\tstartingSpeed: ");
			handler.AppendFormatted(num3);
			handler.AppendLiteral(",");
			stringBuilder13.AppendLine(ref handler);
			if (num4 != 6)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder14 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(20, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tmaxFallSpeed: 0x");
				handler.AppendFormatted(num4, "X2");
				handler.AppendLiteral(",");
				stringBuilder14.AppendLine(ref handler);
			}
			if (num8.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder15 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(31, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tstartingBackgroundColor: 0x");
				handler.AppendFormatted(num8.Value, "X2");
				handler.AppendLiteral(",");
				stringBuilder15.AppendLine(ref handler);
			}
			else
			{
				stringBuilder.AppendLine("\t\t\tstartingBackgroundColor: 0x12,");
			}
			if (num9.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder16 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(27, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tstartingGroundColor: 0x");
				handler.AppendFormatted(num9.Value, "X2");
				handler.AppendLiteral(",");
				stringBuilder16.AppendLine(ref handler);
			}
			else
			{
				stringBuilder.AppendLine("\t\t\tstartingGroundColor: 0x02,");
			}
			int? num10 = null;
			int? num11 = null;
			int? num12 = null;
			int? num13 = null;
			try
			{
				num10 = ParseBox(SpawnYPositionHiTextBox);
			}
			catch
			{
			}
			try
			{
				num11 = ParseBox(SpawnYPositionLowTextBox);
			}
			catch
			{
			}
			try
			{
				num12 = ParseBox(ScrollYPositionHiTextBox);
			}
			catch
			{
			}
			try
			{
				num13 = ParseBox(ScrollYPositionLowTextBox);
			}
			catch
			{
			}
			if (!num10.HasValue)
			{
				num10 = GetNullableInt("LoadedSpawnYPositionHi");
			}
			if (!num11.HasValue)
			{
				num11 = GetNullableInt("LoadedSpawnYPositionLow");
			}
			if (!num12.HasValue)
			{
				num12 = GetNullableInt("LoadedScrollYPositionHi");
			}
			if (!num13.HasValue)
			{
				num13 = GetNullableInt("LoadedScrollYPositionLow");
			}
			if (!num10.HasValue && TryGetCurrentTabValue("LoadedSpawnYPositionHi") is int value7)
			{
				num10 = value7;
			}
			if (!num11.HasValue && TryGetCurrentTabValue("LoadedSpawnYPositionLow") is int value8)
			{
				num11 = value8;
			}
			if (!num12.HasValue && TryGetCurrentTabValue("LoadedScrollYPositionHi") is int value9)
			{
				num12 = value9;
			}
			if (!num13.HasValue && TryGetCurrentTabValue("LoadedScrollYPositionLow") is int value10)
			{
				num13 = value10;
			}
			if (num10.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder17 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(24, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tspawnYPositionHi: 0x");
				handler.AppendFormatted(num10.Value, "X2");
				handler.AppendLiteral(",");
				stringBuilder17.AppendLine(ref handler);
			}
			if (num11.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder18 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tspawnYPositionLow: 0x");
				handler.AppendFormatted(num11.Value, "X2");
				handler.AppendLiteral(",");
				stringBuilder18.AppendLine(ref handler);
			}
			if (num12.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder19 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(25, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tscrollYPositionHi: 0x");
				handler.AppendFormatted(num12.Value, "X2");
				handler.AppendLiteral(",");
				stringBuilder19.AppendLine(ref handler);
			}
			if (num13.HasValue)
			{
				stringBuilder2 = stringBuilder;
				StringBuilder stringBuilder20 = stringBuilder2;
				handler = new StringBuilder.AppendInterpolatedStringHandler(26, 1, stringBuilder2);
				handler.AppendLiteral("\t\t\tscrollYPositionLow: 0x");
				handler.AppendFormatted(num13.Value, "X2");
				handler.AppendLiteral(",");
				stringBuilder20.AppendLine(ref handler);
			}
			bool? flag = null;
			try
			{
				if (ForcePlatformerCheckBox != null && ForcePlatformerCheckBox.IsChecked.HasValue)
				{
					flag = ForcePlatformerCheckBox.IsChecked.Value;
				}
			}
			catch
			{
			}
			if (!flag.HasValue)
			{
				try
				{
					if (TryGetCurrentTabValue("LoadedForcePlatformer") is bool value11)
					{
						flag = value11;
					}
				}
				catch
				{
				}
			}
			if (!flag.HasValue)
			{
				try
				{
					flag = GetBool("LoadedForcePlatformer");
				}
				catch
				{
				}
			}
			if (flag.HasValue && flag.Value)
			{
				stringBuilder.AppendLine("\t\t\tforcePlatformer: true,");
			}
			bool flag2 = false;
			try
			{
				if (TryGetCurrentTabValue("NoParallaxBg") is bool flag3)
				{
					flag2 = flag3;
				}
			}
			catch
			{
			}
			if (!flag2)
			{
				try
				{
					flag2 = GetBool("NoParallaxBg");
				}
				catch
				{
				}
			}
			if (!flag2)
			{
				try
				{
					flag2 = mainWindow2.NoParallaxBg;
				}
				catch
				{
				}
			}
			if (flag2)
			{
				stringBuilder.AppendLine("\t\t\tparallaxDisable: true,");
			}
			try
			{
				if (!string.IsNullOrEmpty(currentTmxPath) && File.Exists(currentTmxPath))
				{
					Match match = Regex.Match(File.ReadAllText(currentTmxPath), "lvlset_([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
					if (match.Success && match.Groups.Count > 1)
					{
						string text9 = match.Groups[1].Value.ToUpperInvariant();
						if (text4 == "A" || text4.Length == 0)
						{
							text4 = text9;
						}
						if (text5 == "A" || text5.Length == 0)
						{
							text5 = text9;
						}
					}
				}
			}
			catch
			{
			}
			if (dictionary.Count > 0)
			{
				stringBuilder.AppendLine("\t\t\tobjectOffsets: [");
				bool flag4 = true;
				foreach (KeyValuePair<(int, int), List<(int, int)>> item4 in dictionary)
				{
					if (!flag4)
					{
						stringBuilder.AppendLine(",");
					}
					flag4 = false;
					stringBuilder.AppendLine("\t\t\t\t{");
					List<(int, int)> value12 = item4.Value;
					if (value12.Count == 1)
					{
						stringBuilder2 = stringBuilder;
						StringBuilder stringBuilder21 = stringBuilder2;
						handler = new StringBuilder.AppendInterpolatedStringHandler(23, 2, stringBuilder2);
						handler.AppendLiteral("\t\t\t\t\tcoordinates: [");
						handler.AppendFormatted(value12[0].Item1);
						handler.AppendLiteral(", ");
						handler.AppendFormatted(value12[0].Item2);
						handler.AppendLiteral("],");
						stringBuilder21.AppendLine(ref handler);
					}
					else
					{
						stringBuilder.AppendLine("\t\t\t\t\tcoordinates: [");
						for (int i = 0; i < value12.Count; i++)
						{
							string value13 = ((i < value12.Count - 1) ? "," : "");
							stringBuilder2 = stringBuilder;
							StringBuilder stringBuilder22 = stringBuilder2;
							handler = new StringBuilder.AppendInterpolatedStringHandler(10, 3, stringBuilder2);
							handler.AppendLiteral("\t\t\t\t\t\t[");
							handler.AppendFormatted(value12[i].Item1);
							handler.AppendLiteral(", ");
							handler.AppendFormatted(value12[i].Item2);
							handler.AppendLiteral("]");
							handler.AppendFormatted(value13);
							stringBuilder22.AppendLine(ref handler);
						}
						stringBuilder.AppendLine("\t\t\t\t\t],");
					}
					if (item4.Key.Item1 != 0 || item4.Key.Item2 != 0)
					{
						if (item4.Key.Item2 != 0)
						{
							string value14 = ((item4.Key.Item2 >= 0) ? $"+{item4.Key.Item2}" : item4.Key.Item2.ToString());
							stringBuilder2 = stringBuilder;
							StringBuilder stringBuilder23 = stringBuilder2;
							handler = new StringBuilder.AppendInterpolatedStringHandler(15, 2, stringBuilder2);
							handler.AppendLiteral("\t\t\t\t\toffsetY: ");
							handler.AppendFormatted(value14);
							handler.AppendFormatted((item4.Key.Item1 != 0) ? "," : "");
							handler.AppendLiteral(" ");
							stringBuilder23.AppendLine(ref handler);
						}
						if (item4.Key.Item1 != 0)
						{
							string value15 = ((item4.Key.Item1 >= 0) ? $"+{item4.Key.Item1}" : item4.Key.Item1.ToString());
							stringBuilder2 = stringBuilder;
							StringBuilder stringBuilder24 = stringBuilder2;
							handler = new StringBuilder.AppendInterpolatedStringHandler(14, 1, stringBuilder2);
							handler.AppendLiteral("\t\t\t\t\toffsetX: ");
							handler.AppendFormatted(value15);
							stringBuilder24.AppendLine(ref handler);
						}
					}
					stringBuilder.Append("\t\t\t\t}");
				}
				stringBuilder.AppendLine();
				stringBuilder.AppendLine("\t\t\t]");
				stringBuilder.AppendLine("\t\t}");
			}
			else
			{
				stringBuilder.AppendLine("\t\t}");
			}
			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
			string path = text + "_metadata.json5";
			string text10 = Path.Combine(folderPath, path);
			File.WriteAllText(text10, stringBuilder.ToString());
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = text10,
					UseShellExecute = true
				});
			}
			catch
			{
			}
			MessageBox.Show("Exported JSON metadata to:\n" + text10 + "\n\nThe file has been opened in your default text editor.", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		catch (Exception ex)
		{
			MessageBox.Show("Error exporting shift JSON: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		bool GetBool(string name)
		{
			object prop = GetProp(name);
			if (prop == null)
			{
				return false;
			}
			try
			{
				return Convert.ToBoolean(prop);
			}
			catch
			{
				return false;
			}
		}
		int? GetNullableInt(string name)
		{
			object prop2 = GetProp(name);
			if (prop2 == null)
			{
				return null;
			}
			try
			{
				return Convert.ToInt32(prop2);
			}
			catch
			{
				return null;
			}
		}
		object? GetProp(string name)
		{
			return mainWindow2.GetType().GetProperty(name)?.GetValue(mainWindow2);
		}
		string GetString(string name, string @default = "")
		{
			return (GetProp(name) as string) ?? @default;
		}
		static int? ParseBox(TextBox? tb)
		{
			try
			{
				if (tb == null)
				{
					return null;
				}
				string text11 = (tb.Text ?? "").Trim();
				if (string.IsNullOrEmpty(text11))
				{
					return null;
				}
				if (text11.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				{
					text11 = text11.Substring(2);
				}
				if (int.TryParse(text11, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
				{
					return result & 0xFF;
				}
				if (int.TryParse(text11, out var result2))
				{
					return result2 & 0xFF;
				}
			}
			catch
			{
			}
			return null;
		}
		object? TryGetCurrentTabValue(string propName)
		{
			try
			{
				FieldInfo field2 = mainWindow2.GetType().GetField("openFiles", BindingFlags.Instance | BindingFlags.NonPublic);
				FieldInfo field3 = mainWindow2.GetType().GetField("currentFileIndex", BindingFlags.Instance | BindingFlags.NonPublic);
				if (field2 != null && field3 != null)
				{
					IList list = field2.GetValue(mainWindow2) as IList;
					object value16 = field3.GetValue(mainWindow2);
					if (list != null && value16 is int num14 && num14 >= 0 && num14 < list.Count)
					{
						object obj24 = list[num14];
						if (obj24 != null)
						{
							PropertyInfo property3 = obj24.GetType().GetProperty(propName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
							if (property3 != null)
							{
								return property3.GetValue(obj24);
							}
							FieldInfo field4 = obj24.GetType().GetField(propName, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
							if (field4 != null)
							{
								return field4.GetValue(obj24);
							}
						}
					}
				}
			}
			catch
			{
			}
			return null;
		}
	}

	private string ConvertJson5ToJson(string json5)
	{
		try
		{
			string[] array = json5.Split('\n');
			for (int i = 0; i < array.Length; i++)
			{
				int num = array[i].IndexOf("//");
				if (num >= 0)
				{
					array[i] = array[i].Substring(0, num);
				}
			}
			json5 = string.Join("\n", array);
			json5 = Regex.Replace(json5, "/\\*.*?\\*/", "", RegexOptions.Singleline);
			json5 = Regex.Replace(json5, "([{,]\\s*)([a-zA-Z_][a-zA-Z0-9_]*)\\s*:", "$1\"$2\":");
			json5 = Regex.Replace(json5, "0x([0-9A-Fa-f]+)", (Match match) => Convert.ToInt32(match.Groups[1].Value, 16).ToString());
			json5 = Regex.Replace(json5, ":\\s*\\+(\\d+)", ": $1");
			json5 = Regex.Replace(json5, ",\\s*\\+(\\d+)", ", $1");
			json5 = Regex.Replace(json5, ",\\s*([}\\]])", "$1");
			return json5;
		}
		catch (Exception ex)
		{
			throw new Exception("JSON5 conversion failed: " + ex.Message);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/setoptionswindow.xaml", UriKind.Relative);
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
			DecoCombo = (ComboBox)target;
			break;
		case 2:
			LockSpritesCheckBox = (CheckBox)target;
			break;
		case 3:
			BlockCombo = (ComboBox)target;
			break;
		case 4:
			StartingBackgroundColorCombo = (ComboBox)target;
			break;
		case 5:
			SpikeCombo = (ComboBox)target;
			break;
		case 6:
			StartingGroundColorCombo = (ComboBox)target;
			break;
		case 7:
			StartingGameModeCombo = (ComboBox)target;
			break;
		case 8:
			ShowAccurateTilesetCheckBox = (CheckBox)target;
			break;
		case 9:
			StartingSpeedCombo = (ComboBox)target;
			break;
		case 10:
			DifficultyCombo = (ComboBox)target;
			break;
		case 11:
			StarsCombo = (ComboBox)target;
			break;
		case 12:
			ForcePlatformerCheckBox = (CheckBox)target;
			break;
		case 13:
			UpperTextBox = (TextBox)target;
			break;
		case 14:
			LowerTextBox = (TextBox)target;
			break;
		case 15:
			ScrollYPositionHiTextBox = (TextBox)target;
			break;
		case 16:
			ScrollYPositionLowTextBox = (TextBox)target;
			break;
		case 17:
			OkButton = (Button)target;
			break;
		case 18:
			CancelButton = (Button)target;
			break;
		case 19:
			BgTintButton = (Button)target;
			break;
		case 20:
			GroundTintButton = (Button)target;
			break;
		case 21:
			TileTintButton = (Button)target;
			break;
		case 22:
			NoParallaxCheckBox = (CheckBox)target;
			break;
		case 23:
			MaxFallSpeedCombo = (ComboBox)target;
			break;
		case 24:
			AttemptJsonLoadButton = (Button)target;
			break;
		case 25:
			RemoveSpriteShiftsButton = (Button)target;
			break;
		case 26:
			ExportShiftJsonButton = (Button)target;
			break;
		case 27:
			SpawnYPositionHiTextBox = (TextBox)target;
			break;
		case 28:
			SpawnYPositionLowTextBox = (TextBox)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
