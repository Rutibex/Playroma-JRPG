namespace Playroma.Sound.Dsp.Analysis;

public static class FastFourierTransform
{
    public static void Forward(Span<float> real, Span<float> imaginary)
    {
        if (real.Length != imaginary.Length)
        {
            throw new ArgumentException("Real and imaginary spans must have equal lengths.");
        }

        int length = real.Length;
        if (length < 2 || (length & (length - 1)) != 0)
        {
            throw new ArgumentException("FFT length must be a power of two and at least two.");
        }

        int j = 0;
        for (int i = 1; i < length; i++)
        {
            int bit = length >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }

            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
            }
        }

        for (int size = 2; size <= length; size <<= 1)
        {
            int half = size >> 1;
            double angleStep = -Math.Tau / size;

            for (int start = 0; start < length; start += size)
            {
                for (int offset = 0; offset < half; offset++)
                {
                    double angle = angleStep * offset;
                    float wr = (float)Math.Cos(angle);
                    float wi = (float)Math.Sin(angle);
                    int even = start + offset;
                    int odd = even + half;
                    float oddReal = (wr * real[odd]) - (wi * imaginary[odd]);
                    float oddImaginary = (wr * imaginary[odd]) + (wi * real[odd]);
                    float evenReal = real[even];
                    float evenImaginary = imaginary[even];
                    real[even] = evenReal + oddReal;
                    imaginary[even] = evenImaginary + oddImaginary;
                    real[odd] = evenReal - oddReal;
                    imaginary[odd] = evenImaginary - oddImaginary;
                }
            }
        }
    }
}
