using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FamidashEditor;

public class OffsetMapWindow : Window, IComponentConnector
{
	internal TextBox ShiftXBox;

	internal TextBox ShiftYBox;

	internal Button CancelButton;

	internal Button ApplyButton;

	private bool _contentLoaded;

	public OffsetMapWindow()
	{
		InitializeComponent();
	}

	private void CancelButton_Click(object sender, RoutedEventArgs e)
	{
		base.DialogResult = false;
		Close();
	}

	private void ApplyButton_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			if (int.TryParse(ShiftXBox.Text.Trim(), out var result) && int.TryParse(ShiftYBox.Text.Trim(), out var result2))
			{
				if (base.Owner is MainWindow mainWindow)
				{
					mainWindow.ApplyMapOffset(result, result2);
				}
				base.DialogResult = true;
				Close();
			}
			else
			{
				MessageBox.Show(this, "Please enter integer values for X and Y shifts.", "Invalid input", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, "Failed to apply offset: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/offsetmapwindow.xaml", UriKind.Relative);
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
			ShiftXBox = (TextBox)target;
			break;
		case 2:
			ShiftYBox = (TextBox)target;
			break;
		case 3:
			CancelButton = (Button)target;
			CancelButton.Click += CancelButton_Click;
			break;
		case 4:
			ApplyButton = (Button)target;
			ApplyButton.Click += ApplyButton_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
