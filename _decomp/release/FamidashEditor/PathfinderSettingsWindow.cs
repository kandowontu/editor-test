using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FamidashEditor;

public class PathfinderSettingsWindow : Window, IComponentConnector
{
	internal RadioButton OptEarliest;

	internal RadioButton OptEarly;

	internal RadioButton OptMiddle;

	internal RadioButton OptLate;

	internal RadioButton OptLatest;

	internal TextBlock BiasLabel;

	internal CheckBox ChkPreferCoins;

	internal CheckBox ChkDrawPathLine;

	internal CheckBox ChkShowLive;

	internal CheckBox ChkShowProspective;

	private bool _contentLoaded;

	public double JumpTimingBias { get; private set; } = 0.5;

	public bool Confirmed { get; private set; }

	public bool PreferCoins { get; private set; }

	public bool DrawPathLine { get; private set; } = true;

	public bool ShowPathfinderLive { get; private set; } = true;

	public bool ShowProspectivePaths { get; private set; }

	public PathfinderSettingsWindow()
	{
		InitializeComponent();
		OptEarliest.Checked += delegate
		{
			UpdateLabel();
		};
		OptEarly.Checked += delegate
		{
			UpdateLabel();
		};
		OptMiddle.Checked += delegate
		{
			UpdateLabel();
		};
		OptLate.Checked += delegate
		{
			UpdateLabel();
		};
		OptLatest.Checked += delegate
		{
			UpdateLabel();
		};
		UpdateLabel();
	}

	private void UpdateLabel()
	{
		if (OptEarliest.IsChecked == true)
		{
			BiasLabel.Text = "Jump at the earliest frame that still survives";
		}
		else if (OptEarly.IsChecked == true)
		{
			BiasLabel.Text = "Jump early — 25% of the way from earliest to latest";
		}
		else if (OptMiddle.IsChecked == true)
		{
			BiasLabel.Text = "Jump at the midpoint between earliest and latest viable timing";
		}
		else if (OptLate.IsChecked == true)
		{
			BiasLabel.Text = "Jump late — 75% of the way from earliest to latest";
		}
		else if (OptLatest.IsChecked == true)
		{
			BiasLabel.Text = "Jump at the last possible frame that still survives";
		}
	}

	private void OkButton_Click(object sender, RoutedEventArgs e)
	{
		if (OptEarliest.IsChecked == true)
		{
			JumpTimingBias = 0.0;
		}
		else if (OptEarly.IsChecked == true)
		{
			JumpTimingBias = 0.25;
		}
		else if (OptMiddle.IsChecked == true)
		{
			JumpTimingBias = 0.5;
		}
		else if (OptLate.IsChecked == true)
		{
			JumpTimingBias = 0.75;
		}
		else if (OptLatest.IsChecked == true)
		{
			JumpTimingBias = 1.0;
		}
		Confirmed = true;
		PreferCoins = ChkPreferCoins.IsChecked == true;
		DrawPathLine = ChkDrawPathLine.IsChecked == true;
		ShowPathfinderLive = ChkShowLive.IsChecked == true;
		ShowProspectivePaths = ChkShowProspective.IsChecked == true;
		base.DialogResult = true;
		Close();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		Confirmed = false;
		base.DialogResult = false;
		Close();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/pathfindersettingswindow.xaml", UriKind.Relative);
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
			OptEarliest = (RadioButton)target;
			break;
		case 2:
			OptEarly = (RadioButton)target;
			break;
		case 3:
			OptMiddle = (RadioButton)target;
			break;
		case 4:
			OptLate = (RadioButton)target;
			break;
		case 5:
			OptLatest = (RadioButton)target;
			break;
		case 6:
			BiasLabel = (TextBlock)target;
			break;
		case 7:
			ChkPreferCoins = (CheckBox)target;
			break;
		case 8:
			ChkDrawPathLine = (CheckBox)target;
			break;
		case 9:
			ChkShowLive = (CheckBox)target;
			break;
		case 10:
			ChkShowProspective = (CheckBox)target;
			break;
		case 11:
			((Button)target).Click += OkButton_Click;
			break;
		case 12:
			((Button)target).Click += CancelButton_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
