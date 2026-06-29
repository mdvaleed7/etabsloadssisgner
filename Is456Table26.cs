using System;

namespace AdvatechEtabsPlugin
{
    public static class Is456Table26
    {
        private static readonly double[] LY_LX_RATIOS = { 1.0, 1.1, 1.2, 1.3, 1.4, 1.5, 1.75, 2.0 };

        // αx+ (positive moment in short span, mid-span)
        private static readonly double[][] TABLE_26_AX_POS = new double[][]
        {
            new double[] {0.024, 0.028, 0.032, 0.036, 0.039, 0.041, 0.045, 0.049}, // Case 1: Interior
            new double[] {0.028, 0.032, 0.036, 0.039, 0.041, 0.044, 0.048, 0.052}, // Case 2: One short edge disc.
            new double[] {0.028, 0.033, 0.039, 0.044, 0.047, 0.051, 0.059, 0.065}, // Case 3: One long edge disc.
            new double[] {0.035, 0.040, 0.045, 0.049, 0.053, 0.056, 0.063, 0.069}, // Case 4: Two adjacent edges disc.
            new double[] {0.035, 0.037, 0.040, 0.043, 0.045, 0.045, 0.049, 0.052}, // Case 5: Two short edges disc.
            new double[] {0.035, 0.043, 0.051, 0.057, 0.063, 0.068, 0.080, 0.088}, // Case 6: Two long edges disc.
            new double[] {0.043, 0.048, 0.053, 0.057, 0.060, 0.064, 0.069, 0.073}, // Case 7: Three edges disc. (1 long cont.)
            new double[] {0.043, 0.051, 0.059, 0.065, 0.071, 0.076, 0.087, 0.096}, // Case 8: Three edges disc. (1 short cont.)
            new double[] {0.056, 0.064, 0.072, 0.079, 0.085, 0.089, 0.100, 0.107}  // Case 9: Four edges disc. (SS)
        };

        // αy+ (positive moment in long span, mid-span) — constant for all Ly/Lx
        private static readonly double[] TABLE_26_AY_POS = { 0.024, 0.028, 0.028, 0.035, 0.035, 0.035, 0.043, 0.043, 0.056 };

        // αx- (negative moment in short span, at supports)
        private static readonly double?[][] TABLE_26_AX_NEG = new double?[][]
        {
            new double?[] {0.032, 0.037, 0.043, 0.047, 0.051, 0.053, 0.060, 0.065}, // Case 1
            new double?[] {0.037, 0.043, 0.048, 0.051, 0.055, 0.057, 0.064, 0.068}, // Case 2
            new double?[] {0.037, 0.044, 0.052, 0.057, 0.063, 0.067, 0.077, 0.085}, // Case 3
            new double?[] {0.047, 0.053, 0.060, 0.065, 0.071, 0.075, 0.084, 0.091}, // Case 4
            new double?[] {0.045, 0.049, 0.052, 0.056, 0.059, 0.060, 0.065, 0.069}, // Case 5
            new double?[] {null, null, null, null, null, null, null, null},          // Case 6: no αx-
            new double?[] {0.057, 0.064, 0.071, 0.076, 0.080, 0.084, 0.091, 0.097}, // Case 7
            new double?[] {null, null, null, null, null, null, null, null},          // Case 8: no αx-
            new double?[] {null, null, null, null, null, null, null, null}           // Case 9: no αx-
        };

        // αy- (negative moment in long span, at supports) — constant where applicable
        private static readonly double?[] TABLE_26_AY_NEG = {
            0.032, // Case 1
            0.037, // Case 2
            0.037, // Case 3
            0.047, // Case 4
            null,  // Case 5: no αy-
            0.045, // Case 6
            null,  // Case 7: no αy-
            0.057, // Case 8
            null   // Case 9: no αy-
        };

        public static double GetAlphaYPos(int boundaryCase)
        {
            if (boundaryCase < 1 || boundaryCase > 9) return 0;
            return TABLE_26_AY_POS[boundaryCase - 1];
        }

        public static double GetAlphaYNeg(int boundaryCase)
        {
            if (boundaryCase < 1 || boundaryCase > 9) return 0;
            return TABLE_26_AY_NEG[boundaryCase - 1] ?? 0;
        }

        public static double GetAlphaXPos(int boundaryCase, double lyLx)
        {
            return InterpolateCoeff(boundaryCase, lyLx, TABLE_26_AX_POS) ?? 0;
        }

        public static double GetAlphaXNeg(int boundaryCase, double lyLx)
        {
            return InterpolateCoeffNullable(boundaryCase, lyLx, TABLE_26_AX_NEG) ?? 0;
        }

        private static double? InterpolateCoeff(int boundaryCase, double lyLx, double[][] table)
        {
            int idx = boundaryCase - 1;
            if (idx < 0 || idx >= table.Length) return null;
            
            double[] row = table[idx];
            double r = Math.Max(1.0, Math.Min(2.0, lyLx));

            for (int i = 0; i < LY_LX_RATIOS.Length - 1; i++)
            {
                if (r >= LY_LX_RATIOS[i] && r <= LY_LX_RATIOS[i + 1])
                {
                    double lo = LY_LX_RATIOS[i];
                    double hi = LY_LX_RATIOS[i + 1];
                    double vLo = row[i];
                    double vHi = row[i + 1];
                    return vLo + (vHi - vLo) * (r - lo) / (hi - lo);
                }
            }
            return row[row.Length - 1];
        }

        private static double? InterpolateCoeffNullable(int boundaryCase, double lyLx, double?[][] table)
        {
            int idx = boundaryCase - 1;
            if (idx < 0 || idx >= table.Length) return null;

            double?[] row = table[idx];
            double r = Math.Max(1.0, Math.Min(2.0, lyLx));

            for (int i = 0; i < LY_LX_RATIOS.Length - 1; i++)
            {
                if (r >= LY_LX_RATIOS[i] && r <= LY_LX_RATIOS[i + 1])
                {
                    double lo = LY_LX_RATIOS[i];
                    double hi = LY_LX_RATIOS[i + 1];
                    double? vLo = row[i];
                    double? vHi = row[i + 1];
                    if (vLo == null || vHi == null) return null;
                    return vLo.Value + (vHi.Value - vLo.Value) * (r - lo) / (hi - lo);
                }
            }
            return row[row.Length - 1];
        }
    }
}
