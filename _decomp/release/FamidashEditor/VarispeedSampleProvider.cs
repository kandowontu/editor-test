using System;
using NAudio.Wave;

namespace FamidashEditor;

public class VarispeedSampleProvider : ISampleProvider
{
	private readonly ISampleProvider source;

	private readonly int channels;

	private float[] buffer;

	private int bufferFrames;

	private int bufferCapacityFrames;

	private double position;

	private double speed = 1.0;

	public WaveFormat WaveFormat { get; }

	public double Speed
	{
		get
		{
			return speed;
		}
		set
		{
			speed = Math.Max(0.001, value);
		}
	}

	public VarispeedSampleProvider(ISampleProvider source, double initialSpeed = 1.0, int prebufferFrames = 8192)
	{
		this.source = source ?? throw new ArgumentNullException("source");
		WaveFormat = source.WaveFormat;
		channels = source.WaveFormat.Channels;
		speed = Math.Max(0.01, initialSpeed);
		bufferCapacityFrames = Math.Max(4, prebufferFrames);
		buffer = new float[bufferCapacityFrames * channels + channels * 4];
		bufferFrames = 0;
		position = 0.0;
	}

	private bool EnsureBuffered(int neededFrames)
	{
		int num = (int)Math.Ceiling(position) + neededFrames + 2;
		if (num <= bufferFrames)
		{
			return true;
		}
		int num2 = num - bufferFrames;
		if (bufferFrames + num2 > bufferCapacityFrames)
		{
			int num3 = Math.Max(bufferCapacityFrames * 2, bufferFrames + num2 + 4);
			Array.Resize(ref buffer, num3 * channels + channels * 4);
			bufferCapacityFrames = num3;
		}
		float[] array = new float[num2 * channels];
		int i = 0;
		try
		{
			int num4;
			for (; i < array.Length; i += num4)
			{
				num4 = source.Read(array, i, array.Length - i);
				if (num4 <= 0)
				{
					break;
				}
			}
		}
		catch
		{
		}
		int num5 = i / channels;
		if (num5 <= 0)
		{
			return false;
		}
		Array.Copy(array, 0, buffer, bufferFrames * channels, num5 * channels);
		bufferFrames += num5;
		return true;
	}

	public int Read(float[] outBuffer, int offset, int count)
	{
		if (outBuffer == null)
		{
			throw new ArgumentNullException("outBuffer");
		}
		if (count % channels != 0)
		{
			throw new ArgumentException("count must be a multiple of channels");
		}
		int num = count / channels;
		int num2 = 0;
		for (int i = 0; i < num; i++)
		{
			if (!EnsureBuffered(1))
			{
				break;
			}
			int num3 = (int)Math.Floor(position);
			double num4 = position - (double)num3;
			if (num3 + 1 >= bufferFrames)
			{
				break;
			}
			for (int j = 0; j < channels; j++)
			{
				float num5 = buffer[num3 * channels + j];
				float num6 = buffer[(num3 + 1) * channels + j];
				float num7 = (float)((double)num5 + num4 * (double)(num6 - num5));
				outBuffer[offset + num2 * channels + j] = num7;
			}
			num2++;
			position += speed;
			if (position > 1024.0 && bufferFrames > 2048)
			{
				int num8 = (int)position - 512;
				if (num8 > 0)
				{
					int num9 = bufferFrames - num8;
					Array.Copy(buffer, num8 * channels, buffer, 0, num9 * channels);
					bufferFrames = num9;
					position -= num8;
				}
			}
		}
		int num10 = num2 * channels;
		if (num10 < count)
		{
			Array.Clear(outBuffer, offset + num10, count - num10);
		}
		return num10;
	}
}
