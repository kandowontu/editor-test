using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace FamidashEditor
{
    // Simple varispeed ISampleProvider that changes playback speed by resampling
    // source frames with linear interpolation. This changes pitch (not pitch-preserving),
    // but it's pure managed and has no external dependencies.
    public class VarispeedSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider source;
        private readonly int channels;
        private float[] buffer; // interleaved frames
        private int bufferFrames; // number of frames currently in buffer
        private int bufferCapacityFrames;
        private double position; // fractional frame index within buffer
        private double speed = 1.0;

        public VarispeedSampleProvider(ISampleProvider source, double initialSpeed = 1.0, int prebufferFrames = 8192)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.WaveFormat = source.WaveFormat;
            this.channels = source.WaveFormat.Channels;
            this.speed = Math.Max(0.01, initialSpeed);
            bufferCapacityFrames = Math.Max(4, prebufferFrames);
            buffer = new float[bufferCapacityFrames * channels + channels * 4];
            bufferFrames = 0;
            position = 0.0;
        }

        public WaveFormat WaveFormat { get; }

        public double Speed
        {
            get => speed;
            set => speed = Math.Max(0.001, value);
        }

        // Ensure buffer has at least 'neededFrames' frames available after current position
        private bool EnsureBuffered(int neededFrames)
        {
            int neededIndex = (int)Math.Ceiling(position) + neededFrames + 2; // +2 for interpolation
            if (neededIndex <= bufferFrames) return true;

            int framesToRead = neededIndex - bufferFrames;
            // grow buffer if necessary
            if (bufferFrames + framesToRead > bufferCapacityFrames)
            {
                int newCap = Math.Max(bufferCapacityFrames * 2, bufferFrames + framesToRead + 4);
                Array.Resize(ref buffer, newCap * channels + channels * 4);
                bufferCapacityFrames = newCap;
            }

            float[] readBuf = new float[framesToRead * channels];
            int read = 0;
            try
            {
                while (read < readBuf.Length)
                {
                    int r = source.Read(readBuf, read, readBuf.Length - read);
                    if (r <= 0) break;
                    read += r;
                }
            }
            catch { }

            int framesRead = read / channels;
            if (framesRead <= 0) return false;
            Array.Copy(readBuf, 0, buffer, bufferFrames * channels, framesRead * channels);
            bufferFrames += framesRead;
            return true;
        }

        public int Read(float[] outBuffer, int offset, int count)
        {
            if (outBuffer == null) throw new ArgumentNullException(nameof(outBuffer));
            if (count % channels != 0) throw new ArgumentException("count must be a multiple of channels");

            int framesRequested = count / channels;
            int framesProduced = 0;

            for (int f = 0; f < framesRequested; f++)
            {
                // Need two source frames for interpolation
                if (!EnsureBuffered(1))
                {
                    // No more source samples
                    break;
                }

                int index = (int)Math.Floor(position);
                double frac = position - index;
                if (index + 1 >= bufferFrames)
                {
                    // Not enough data to interpolate
                    break;
                }

                for (int c = 0; c < channels; c++)
                {
                    float a = buffer[(index * channels) + c];
                    float b = buffer[((index + 1) * channels) + c];
                    float sample = (float)(a + frac * (b - a));
                    outBuffer[offset + framesProduced * channels + c] = sample;
                }

                framesProduced++;
                position += speed;

                // Trim consumed frames from buffer to avoid unbounded growth
                if (position > 1024 && bufferFrames > 2048)
                {
                    int drop = (int)position - 512;
                    if (drop > 0)
                    {
                        int remaining = bufferFrames - drop;
                        Array.Copy(buffer, drop * channels, buffer, 0, remaining * channels);
                        bufferFrames = remaining;
                        position -= drop;
                    }
                }
            }

            // zero-fill remainder
            int samplesProduced = framesProduced * channels;
            if (samplesProduced < count)
            {
                Array.Clear(outBuffer, offset + samplesProduced, count - samplesProduced);
            }

            return samplesProduced;
        }
    }
}
