using System;

// One ball per loaded round, capped at the native five-ball burst.
public static class VanderBallBurstMath
{
    public static int Count(float magazine, float ammoPerBall, int maximum = 5)
    {
        if (maximum <= 0 || float.IsNaN(magazine) || float.IsInfinity(magazine) ||
            float.IsNaN(ammoPerBall) || float.IsInfinity(ammoPerBall) || ammoPerBall < 0) return 0;
        if (ammoPerBall == 0) return maximum;
        return (int)Math.Max(0, Math.Min(maximum, Math.Floor((magazine + .001f) / ammoPerBall)));
    }
}
