using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FamidashEditor;

public class LoadingWindow : Window, IComponentConnector
{
	internal TextBlock MessageText;

	private bool _contentLoaded;

	public LoadingWindow()
	{
		InitializeComponent();
	}

	public void SetMessage(string message)
	{
		if (MessageText != null)
		{
			MessageText.Text = message;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/loadingwindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		if (connectionId == 1)
		{
			MessageText = (TextBlock)target;
		}
		else
		{
			_contentLoaded = true;
		}
	}
}
