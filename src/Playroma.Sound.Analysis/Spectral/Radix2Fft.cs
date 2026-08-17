using System.Numerics;

namespace Playroma.Sound.Analysis.Spectral;

internal static class Radix2Fft
{
    public static void Transform(Span<Complex> buffer)
    {
        int length = buffer.Length;
        if (length < 2 || (length & (length - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.", nameof(buffer));
        int j = 0;
        for (int i = 1; i < length; i++)
        {
            int bit = length >> 1;
            while ((j & bit) != 0) { j ^= bit; bit >>= 1; }
            j ^= bit;
            if (i < j) (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
        }
        for (int size = 2; size <= length; size <<= 1)
        {
            double angle = -2.0 * Math.PI / size;
            Complex step = new(Math.Cos(angle), Math.Sin(angle));
            int half = size >> 1;
            for (int start = 0; start < length; start += size)
            {
                Complex twiddle = Complex.One;
                for (int offset = 0; offset < half; offset++)
                {
                    Complex even = buffer[start + offset];
                    Complex odd = buffer[start + offset + half] * twiddle;
                    buffer[start + offset] = even + odd;
                    buffer[start + offset + half] = even - odd;
                    twiddle *= step;
                }
            }
        }
    }
}
