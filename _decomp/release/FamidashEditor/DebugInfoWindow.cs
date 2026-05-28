using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FamidashEditor;

public class DebugInfoWindow : Window, IComponentConnector
{
	internal TextBlock DbgGameModeText;

	internal TextBlock DbgMiniText;

	internal TextBlock DbgInvertedGravityText;

	internal TextBlock DbgXPosText;

	internal TextBlock DbgYPosText;

	internal TextBlock DbgXSpeedText;

	internal TextBlock DbgNinjaJumpsText;

	internal TextBlock DbgDBlockedText;

	internal TextBlock DbgHBlockedText;

	internal TextBlock DbgFBlockedText;

	internal TextBlock DbgJBlockedText;

	internal TextBlock DbgOrbedText;

	internal TextBlock DbgBlackOrbedText;

	internal TextBlock DbgDashingText;

	internal TextBlock DbgRobotJumpTimeText;

	internal TextBlock DbgPlayerYVelText;

	internal TextBlock DbgBufferActiveText;

	internal TextBlock DbgFreeCamText;

	private bool _contentLoaded;

	public DebugInfoWindow()
	{
		InitializeComponent();
	}

	public void UpdateDebugInfo(int gameMode, bool mini, bool invertedGravity, int xPos, int yPos, string xSpeed, int ninjaJumps, bool dBlocked, bool hBlocked, bool fBlocked, bool jBlocked, bool orbed, bool blackOrbed, int dashing, int robotJumpTime, int playerYVel, bool bufferActive, bool freeCam)
	{
		try
		{
			base.Dispatcher.Invoke(delegate
			{
				if (DbgGameModeText != null)
				{
					DbgGameModeText.Text = $"Game mode: {gameMode}";
				}
				if (DbgMiniText != null)
				{
					DbgMiniText.Text = $"Mini: {mini}";
				}
				if (DbgInvertedGravityText != null)
				{
					DbgInvertedGravityText.Text = $"Inverted gravity: {invertedGravity}";
				}
				if (DbgXPosText != null)
				{
					DbgXPosText.Text = $"X position: {xPos} (0x{xPos:X})";
				}
				if (DbgYPosText != null)
				{
					DbgYPosText.Text = $"Y position: {yPos} (0x{yPos:X})";
				}
				if (DbgXSpeedText != null)
				{
					DbgXSpeedText.Text = "X speed: " + xSpeed;
				}
				if (DbgNinjaJumpsText != null)
				{
					DbgNinjaJumpsText.Text = $"Ninja jumps: {ninjaJumps}";
				}
				if (DbgDBlockedText != null)
				{
					DbgDBlockedText.Text = $"D blocked: {dBlocked}";
				}
				if (DbgHBlockedText != null)
				{
					DbgHBlockedText.Text = $"H blocked: {hBlocked}";
				}
				if (DbgFBlockedText != null)
				{
					DbgFBlockedText.Text = $"F blocked: {fBlocked}";
				}
				if (DbgJBlockedText != null)
				{
					DbgJBlockedText.Text = $"J blocked: {jBlocked}";
				}
				if (DbgOrbedText != null)
				{
					DbgOrbedText.Text = $"Orbed: {orbed}";
				}
				if (DbgBlackOrbedText != null)
				{
					DbgBlackOrbedText.Text = $"Black orbed: {blackOrbed}";
				}
				if (DbgDashingText != null)
				{
					DbgDashingText.Text = $"Dashing: {dashing}";
				}
				if (DbgRobotJumpTimeText != null)
				{
					DbgRobotJumpTimeText.Text = $"Robot jump time: {robotJumpTime}";
				}
				if (DbgPlayerYVelText != null)
				{
					DbgPlayerYVelText.Text = $"Player Y vel: {playerYVel} (0x{playerYVel:X})";
				}
				if (DbgBufferActiveText != null)
				{
					DbgBufferActiveText.Text = $"Buffer active: {bufferActive}";
				}
				if (DbgFreeCamText != null)
				{
					DbgFreeCamText.Text = $"FreeCam: {freeCam}";
				}
			});
		}
		catch
		{
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "8.0.25.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/FamidashEditor;component/debuginfowindow.xaml", UriKind.Relative);
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
			DbgGameModeText = (TextBlock)target;
			break;
		case 2:
			DbgMiniText = (TextBlock)target;
			break;
		case 3:
			DbgInvertedGravityText = (TextBlock)target;
			break;
		case 4:
			DbgXPosText = (TextBlock)target;
			break;
		case 5:
			DbgYPosText = (TextBlock)target;
			break;
		case 6:
			DbgXSpeedText = (TextBlock)target;
			break;
		case 7:
			DbgNinjaJumpsText = (TextBlock)target;
			break;
		case 8:
			DbgDBlockedText = (TextBlock)target;
			break;
		case 9:
			DbgHBlockedText = (TextBlock)target;
			break;
		case 10:
			DbgFBlockedText = (TextBlock)target;
			break;
		case 11:
			DbgJBlockedText = (TextBlock)target;
			break;
		case 12:
			DbgOrbedText = (TextBlock)target;
			break;
		case 13:
			DbgBlackOrbedText = (TextBlock)target;
			break;
		case 14:
			DbgDashingText = (TextBlock)target;
			break;
		case 15:
			DbgRobotJumpTimeText = (TextBlock)target;
			break;
		case 16:
			DbgPlayerYVelText = (TextBlock)target;
			break;
		case 17:
			DbgBufferActiveText = (TextBlock)target;
			break;
		case 18:
			DbgFreeCamText = (TextBlock)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
