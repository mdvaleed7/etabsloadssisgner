using System;

namespace CSiNET8PluginExample1
{
    public static class EffectiveSpanCalculator
    {
        public static double CalculateEffectiveSpan(
            double ccSpan, 
            double supportW1, 
            double supportW2, 
            bool isCont1, 
            bool isCont2, 
            double effectiveDepth,
            SlabType type)
        {
            // Calculate Clear Span
            double clearSpan = ccSpan - (supportW1 / 2.0) - (supportW2 / 2.0);
            if (clearSpan <= 0) clearSpan = ccSpan; // Fallback if no supports or tiny span

            if (type == SlabType.Cantilever)
            {
                // c) Cantilever - effective length = length to face of support + d/2
                // Or if continuous, length to centre of support.
                double supportWidth = supportW1 > 0 ? supportW1 : supportW2;
                bool isCont = isCont1 || isCont2;
                if (isCont)
                    return clearSpan + (supportWidth / 2.0);
                else
                    return clearSpan + (effectiveDepth / 2.0);
            }

            // Normal Slabs
            // If supports are less than 1/12 of clear span, treat as simply supported (a)
            double maxSupport = Math.Max(supportW1, supportW2);
            if (maxSupport < clearSpan / 12.0)
            {
                // a) Simply supported
                double d_added = clearSpan + effectiveDepth;
                double c_c = ccSpan; // which is clearSpan + W1/2 + W2/2
                return Math.Min(d_added, c_c);
            }
            else
            {
                // b) Continuous beam or slab (supports wider than L/12 or 600mm)
                if (isCont1 && isCont2)
                {
                    // 1) For intermediate spans or one end fixed
                    return clearSpan;
                }
                else if ((isCont1 && !isCont2) || (!isCont1 && isCont2))
                {
                    // 2) End span with one end free and other continuous
                    double freeSupportWidth = !isCont1 ? supportW1 : supportW2;
                    double opt1 = clearSpan + (effectiveDepth / 2.0);
                    double opt2 = clearSpan + (freeSupportWidth / 2.0);
                    return Math.Min(opt1, opt2);
                }
                else
                {
                    // Default fallback (e.g. 4-edge discontinuous)
                    double d_added = clearSpan + effectiveDepth;
                    double c_c = ccSpan;
                    return Math.Min(d_added, c_c);
                }
            }
        }
    }
}
