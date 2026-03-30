using System.Runtime.CompilerServices;

public sealed class OpenSimplex2F
{
    private const long PrimeX = 0x5205402B9270C86FL;
    private const long PrimeY = 0x598CD327003817B5L;
    private const long HashMultiplier = 0x53A3F72DEEC546F5L;
    private const double Root2Over2 = 0.7071067811865476;
    private const double Skew2D = 0.366025403784439;
    private const double Unskew2D = -0.21132486540518713;
    private const int GradientExponent2D = 7;
    private const int GradientCount2D = 1 << GradientExponent2D;
    private const double Normalizer2D = 0.01001634121365712;
    private const float RadiusSquared2D = 0.5f;

    private static readonly float[] Gradients2D;

    private readonly long seed;
    private readonly float frequency;
    private readonly bool improveX;

    static OpenSimplex2F()
    {
        Gradients2D = new float[GradientCount2D * 2];
        float[] grad2 =
        {
             0.38268343236509f,   0.923879532511287f,
             0.923879532511287f,  0.38268343236509f,
             0.923879532511287f, -0.38268343236509f,
             0.38268343236509f,  -0.923879532511287f,
            -0.38268343236509f,  -0.923879532511287f,
            -0.923879532511287f, -0.38268343236509f,
            -0.923879532511287f,  0.38268343236509f,
            -0.38268343236509f,   0.923879532511287f,
             0.130526192220052f,  0.99144486137381f,
             0.608761429008721f,  0.793353340291235f,
             0.793353340291235f,  0.608761429008721f,
             0.99144486137381f,   0.130526192220051f,
             0.99144486137381f,  -0.130526192220051f,
             0.793353340291235f, -0.60876142900872f,
             0.608761429008721f, -0.793353340291235f,
             0.130526192220052f, -0.99144486137381f,
            -0.130526192220052f, -0.99144486137381f,
            -0.608761429008721f, -0.793353340291235f,
            -0.793353340291235f, -0.608761429008721f,
            -0.99144486137381f,  -0.130526192220052f,
            -0.99144486137381f,   0.130526192220051f,
            -0.793353340291235f,  0.608761429008721f,
            -0.608761429008721f,  0.793353340291235f,
            -0.130526192220052f,  0.99144486137381f,
        };

        for (int i = 0; i < grad2.Length; i++)
            grad2[i] = (float)(grad2[i] / Normalizer2D);

        for (int i = 0, j = 0; i < Gradients2D.Length; i++, j++)
        {
            if (j == grad2.Length)
                j = 0;

            Gradients2D[i] = grad2[j];
        }
    }

    public OpenSimplex2F(long seed, float frequency, bool improveX = true)
    {
        this.seed = seed;
        this.frequency = frequency;
        this.improveX = improveX;
    }

    public float GetNoise(float x, float y)
    {
        double scaledX = x * frequency;
        double scaledY = y * frequency;
        return improveX
            ? Noise2ImproveX(seed, scaledX, scaledY)
            : Noise2(seed, scaledX, scaledY);
    }

    private static float Noise2(long seed, double x, double y)
    {
        double s = Skew2D * (x + y);
        return Noise2UnskewedBase(seed, x + s, y + s);
    }

    private static float Noise2ImproveX(long seed, double x, double y)
    {
        double xx = x * Root2Over2;
        double yy = y * (Root2Over2 * (1 + (2 * Skew2D)));
        return Noise2UnskewedBase(seed, yy + xx, yy - xx);
    }

    private static float Noise2UnskewedBase(long seed, double xs, double ys)
    {
        int xsb = FastFloor(xs);
        int ysb = FastFloor(ys);
        float xi = (float)(xs - xsb);
        float yi = (float)(ys - ysb);

        long xsbp = xsb * PrimeX;
        long ysbp = ysb * PrimeY;

        float t = (xi + yi) * (float)Unskew2D;
        float dx0 = xi + t;
        float dy0 = yi + t;

        float value = 0f;
        float a0 = RadiusSquared2D - (dx0 * dx0) - (dy0 * dy0);
        if (a0 > 0f)
            value = (a0 * a0) * (a0 * a0) * Grad(seed, xsbp, ysbp, dx0, dy0);

        float a1 = (float)(2 * (1 + (2 * Unskew2D)) * ((1 / Unskew2D) + 2)) * t
            + ((float)(-2 * (1 + (2 * Unskew2D)) * (1 + (2 * Unskew2D))) + a0);
        if (a1 > 0f)
        {
            float dx1 = dx0 - (float)(1 + (2 * Unskew2D));
            float dy1 = dy0 - (float)(1 + (2 * Unskew2D));
            value += (a1 * a1) * (a1 * a1) * Grad(seed, xsbp + PrimeX, ysbp + PrimeY, dx1, dy1);
        }

        if (dy0 > dx0)
        {
            float dx2 = dx0 - (float)Unskew2D;
            float dy2 = dy0 - (float)(Unskew2D + 1);
            float a2 = RadiusSquared2D - (dx2 * dx2) - (dy2 * dy2);
            if (a2 > 0f)
                value += (a2 * a2) * (a2 * a2) * Grad(seed, xsbp, ysbp + PrimeY, dx2, dy2);
        }
        else
        {
            float dx2 = dx0 - (float)(Unskew2D + 1);
            float dy2 = dy0 - (float)Unskew2D;
            float a2 = RadiusSquared2D - (dx2 * dx2) - (dy2 * dy2);
            if (a2 > 0f)
                value += (a2 * a2) * (a2 * a2) * Grad(seed, xsbp + PrimeX, ysbp, dx2, dy2);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Grad(long seed, long xsvp, long ysvp, float dx, float dy)
    {
        long hash = seed ^ xsvp ^ ysvp;
        hash *= HashMultiplier;
        hash ^= hash >> (64 - GradientExponent2D + 1);
        int gi = (int)hash & ((GradientCount2D - 1) << 1);
        return (Gradients2D[gi] * dx) + (Gradients2D[gi + 1] * dy);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastFloor(double x)
    {
        int xi = (int)x;
        return x < xi ? xi - 1 : xi;
    }
}
