using System;
using System.Collections.Generic;

namespace FamidashEditor;

internal sealed class MesenRamCaptureOptions
{
	public string MesenExePath { get; set; } = string.Empty;

	public string RomPath { get; set; } = string.Empty;

	public string OutputPath { get; set; } = string.Empty;

	public int TimeoutSeconds { get; set; } = 180;

	public int MaxFrames { get; set; } = 1200;

	public int SampleEveryNFrames { get; set; } = 1;

	public IReadOnlyList<ushort> Addresses { get; set; } = Array.Empty<ushort>();
}
