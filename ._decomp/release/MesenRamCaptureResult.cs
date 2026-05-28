namespace FamidashEditor;

internal sealed class MesenRamCaptureResult
{
	public int ExitCode { get; set; }

	public int StopCode { get; set; }

	public int Samples { get; set; }

	public string StdErr { get; set; } = string.Empty;

}
