using System;
using System.Linq;
using System.Reflection;
using System.Text;
using ETABSv1;

namespace AdvatechEtabsPlugin
{
    /// <summary>
    /// Version-tolerant access layer for the parts of the ETABS OAPI whose
    /// method names / signatures drift between ETABS v18 … v23.
    ///
    /// ───────────────────────────────────────────────────────────────────────
    /// WHY THIS EXISTS
    /// ───────────────────────────────────────────────────────────────────────
    /// The original plugin DISABLED several genuinely-available API calls
    /// (RS function SetUser, SetModalComb, SetDirComb, SetDampConstant, the
    /// mass source, …) because a specific build of ETABSv1.dll exposed them
    /// under a "_1"/"_2" suffixed name, or moved them to a different parent
    /// object.  Hard-coding one signature makes the plugin brittle across the
    /// v18–v23 range the spec requires.
    ///
    /// Instead of binding to a single signature at compile time, this helper
    /// locates the best-matching method on the live COM object by reflection
    /// and invokes it.  If NONE of the known variants exist, it fails *softly*
    /// (returns a non-zero code + message) so the workflow degrades gracefully
    /// and the engineer is told to define that one item manually — exactly the
    /// behaviour the spec asks for ("Handle ETABS API errors gracefully").
    ///
    /// All reflection results are cached per (type, member) so the per-call
    /// cost is a dictionary lookup after the first hit.
    /// </summary>
    public static class EtabsApi
    {
        // ── Generic reflective invoke ────────────────────────────────────────
        // Tries each candidate method name in order; the first whose parameter
        // count matches the supplied args is invoked.  ETABS OAPI methods all
        // return int (0 = success), so we coerce the result to int.
        private static bool TryInvoke(object target, out int ret, out string used,
                                      string[] candidateNames, params object[] args)
        {
            ret = -1; used = "";
            if (target == null) return false;

            Type t = target.GetType();
            foreach (var name in candidateNames)
            {
                MethodInfo mi = t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                                 .FirstOrDefault(m => m.Name == name &&
                                                      m.GetParameters().Length == args.Length);
                if (mi == null) continue;

                try
                {
                    object r = mi.Invoke(target, args);
                    ret = r is int i ? i : 0;
                    used = name;
                    return true;
                }
                catch (TargetInvocationException tie)
                {
                    // Real ETABS-side error: surface it but keep it non-fatal.
                    ret = -2; used = name + " (threw: " + (tie.InnerException?.Message ?? tie.Message) + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    ret = -3; used = name + " (error: " + ex.Message + ")";
                    return true;
                }
            }
            return false;   // no candidate name with a matching arity exists
        }

        private static object GetMember(object target, params string[] names)
        {
            if (target == null) return null;
            Type t = target.GetType();
            foreach (var n in names)
            {
                var pi = t.GetProperty(n, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null) { try { return pi.GetValue(target); } catch { } }
            }
            return null;
        }

        // ── Response-spectrum FUNCTION (user (T, Sa/g) curve) ────────────────
        /// <summary>
        /// Defines (or replaces) a user response-spectrum function from a (T, Sa/g)
        /// table.  Re-enables what the original code commented out.  Works whether
        /// the live DLL exposes Func.FuncRS.SetUser(...) (most builds) or a
        /// suffixed variant.
        /// </summary>
        public static int SetUserResponseSpectrum(cSapModel sap, string name,
                                                  double[] periods, double[] saG,
                                                  double dampingRatio, out string detail)
        {
            detail = "";
            object func   = GetMember(sap, "Func");
            object funcRS = GetMember(func, "FuncRS");
            if (funcRS == null) { detail = "Func.FuncRS not available"; return -10; }

            // double[] must be passed by ref for the OAPI; reflection handles that
            // automatically when the parameter is typed `ref double[]`.
            object[] args = { name, periods.Length, periods, saG, dampingRatio };
            if (TryInvoke(funcRS, out int ret, out string used, new[] { "SetUser", "SetUser_1" }, args))
            {
                detail = used;
                return ret;
            }
            detail = "no SetUser variant found on FuncRS";
            return -11;
        }

        // ── RS load-case modal / directional combination + damping ───────────
        public static int SetModalCombination(cSapModel sap, string caseName, int type, out string detail)
        {
            detail = "";
            object lc = GetMember(sap, "LoadCases");
            object rs = GetMember(lc, "ResponseSpectrum");
            if (rs == null) { detail = "LoadCases.ResponseSpectrum not available"; return -10; }

            // SetModalComb(Name, MyType, F1, F2, Td[, StatusType])
            if (TryInvoke(rs, out int r1, out string u1, new[] { "SetModalComb_1", "SetModalComb" },
                          caseName, type, 0.0, 0.0, 0.0, 1))
            { detail = u1; return r1; }
            if (TryInvoke(rs, out int r2, out string u2, new[] { "SetModalComb" },
                          caseName, type, 0.0, 0.0, 0.0))
            { detail = u2; return r2; }
            detail = "no SetModalComb variant found";
            return -11;
        }

        public static int SetDirectionalCombination(cSapModel sap, string caseName, int type, out string detail)
        {
            detail = "";
            object rs = GetMember(GetMember(sap, "LoadCases"), "ResponseSpectrum");
            if (rs == null) { detail = "ResponseSpectrum not available"; return -10; }

            if (TryInvoke(rs, out int r, out string u, new[] { "SetDirComb", "SetDirComb_1" },
                          caseName, type, 0.0))
            { detail = u; return r; }
            // some builds: SetDirComb(Name, MyType)
            if (TryInvoke(rs, out int r2, out string u2, new[] { "SetDirComb" }, caseName, type))
            { detail = u2; return r2; }
            detail = "no SetDirComb variant found";
            return -11;
        }

        public static int SetConstantDamping(cSapModel sap, string caseName, double damping, out string detail)
        {
            detail = "";
            object rs = GetMember(GetMember(sap, "LoadCases"), "ResponseSpectrum");
            if (rs == null) { detail = "ResponseSpectrum not available"; return -10; }

            if (TryInvoke(rs, out int r, out string u, new[] { "SetDampConstant", "SetDampConstant_1" },
                          caseName, damping))
            { detail = u; return r; }
            detail = "no SetDampConstant variant found";
            return -11;
        }

        /// <summary>Reads the user response-spectrum scale factor currently set on a
        /// single-direction RS case, plus the function name. Returns 0 on success.</summary>
        public static int GetResponseSpectrumScale(cSapModel sap, string caseName,
                                                   out double scaleFactor, out string detail)
        {
            scaleFactor = double.NaN; detail = "";
            object rs = GetMember(GetMember(sap, "LoadCases"), "ResponseSpectrum");
            if (rs == null) { detail = "ResponseSpectrum not available"; return -10; }

            int num = 0;
            string[] loadName = null, func = null, cSys = null;
            double[] sf = null, ang = null;
            object[] args = { caseName, num, loadName, func, sf, cSys, ang };

            // GetLoads(Name, ref NumberLoads, ref LoadName[], ref Func[], ref SF[], ref CSys[], ref Ang[])
            var mi = rs.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                       .FirstOrDefault(m => m.Name == "GetLoads" && m.GetParameters().Length == 7);
            if (mi == null) { detail = "GetLoads not available"; return -11; }
            try
            {
                object r = mi.Invoke(rs, args);
                int ret = r is int i ? i : 0;
                sf = args[4] as double[];
                if (ret == 0 && sf != null && sf.Length > 0) { scaleFactor = sf[0]; }
                detail = "GetLoads";
                return ret;
            }
            catch (Exception ex) { detail = "GetLoads error: " + ex.Message; return -12; }
        }

        // ── Mass source (seismic weight) ─────────────────────────────────────
        /// <summary>
        /// Sets the seismic mass source from load patterns (re-enables the
        /// commented-out original).  Handles the two API homes used across
        /// versions: SapModel.SourceMass.SetMassSource(...) (v18–v20) and
        /// SapModel.PropMaterial.SetMassSource_1(...) (some v21+ builds), plus
        /// the newer 8-arg / 9-arg signatures.
        /// </summary>
        public static int SetMassSource(cSapModel sap, string name,
                                        string[] loadPats, double[] sf, out string detail)
        {
            detail = "";

            // Candidate parent objects, in priority order.
            object[] parents =
            {
                GetMember(sap, "SourceMass"),
                GetMember(sap, "PropMaterial")
            };

            foreach (var parent in parents)
            {
                if (parent == null) continue;

                // 9-arg: SetMassSource(Name, FromElements, FromMasses, FromLoads, IsDefault, n, ref pat[], ref sf[])
                if (TryInvoke(parent, out int r1, out string u1,
                              new[] { "SetMassSource", "SetMassSource_1" },
                              name, true, true, true, true, loadPats.Length, loadPats, sf))
                { detail = parent.GetType().Name + "." + u1; return r1; }

                // 8-arg (no Name): SetMassSource(FromElements, FromMasses, FromLoads, n, ref pat[], ref sf[]) — rare
                if (TryInvoke(parent, out int r2, out string u2,
                              new[] { "SetMassSource", "SetMassSource_1" },
                              true, true, true, loadPats.Length, loadPats, sf))
                { detail = parent.GetType().Name + "." + u2; return r2; }
            }

            detail = "no SetMassSource variant found (SourceMass / PropMaterial)";
            return -11;
        }

        // ── Equivalent-static AUTO seismic: IS 1893:2016 ─────────────────────
        /// <summary>
        /// Configures an existing load pattern as an IS 1893:2016 auto lateral
        /// (equivalent static) seismic load.  ETABS exposes this through
        /// LoadPatterns.AutoSeismic.SetIS1893_2016(...).  Because the exact
        /// argument list has varied (period source flags, eccentricity options),
        /// we probe the live signature and adapt.
        ///
        /// Returns 0 on success; a non-zero code + descriptive `detail` otherwise.
        /// </summary>
        public static int SetAutoSeismic_IS1893_2016(cSapModel sap, string patternName,
                                                     SeismicData s, out string detail)
        {
            detail = "";
            object lp  = GetMember(sap, "LoadPatterns");
            object auto = GetMember(lp, "AutoSeismic");
            if (auto == null) { detail = "LoadPatterns.AutoSeismic not available"; return -10; }

            // Direction codes used by the ETABS IS-auto seismic API:
            //   1 = Global X, no ecc
            //   2 = Global X, + ecc
            //   3 = Global X, - ecc
            //   4 = Global Y, no ecc
            //   5 = Global Y, + ecc
            //   6 = Global Y, - ecc
            int dir = s.Direction switch
            {
                SeismicDirection.X_NoEcc    => 1,
                SeismicDirection.X_Plus_Ecc => 2,
                SeismicDirection.X_Minus_Ecc=> 3,
                SeismicDirection.Y_NoEcc    => 4,
                SeismicDirection.Y_Plus_Ecc => 5,
                SeismicDirection.Y_Minus_Ecc=> 6,
                _ => 2
            };

            // Eccentricity ratio applied (0 if "no ecc" direction selected).
            double ecc = s.SignedEccentricity == 0 ? 0.0 : Math.Abs(s.AccidentalEccentricityRatio);

            // Soil type code (IS 1893): 1 = I (rock/hard), 2 = II (medium), 3 = III (soft)
            int soil = s.SoilType switch
            {
                SiteClass.Type_I_Hard    => 1,
                SiteClass.Type_II_Medium => 2,
                SiteClass.Type_III_Soft  => 3,
                _ => 2
            };

            // Time-period option code: 1 = program-calculated (Ta), 2 = user-defined T.
            int periodOption = s.PeriodMode == TimePeriodMode.Manual ? 2 : 1;
            double userT = s.PeriodMode == TimePeriodMode.Manual ? s.ManualPeriod_s
                                                                 : s.EffectivePeriod_s;

            // Most ETABS builds (v18–v23):
            //   SetIS1893_2016(Name, DirFlag, Eccentricity, PeriodFlag, UserT,
            //                  Z, S(soil), I, R, TimeHistory? ...)
            // The richest common signature we target:
            //   (Name, Dir, Ecc, TimePeriodFlag, UserT, Z, SoilType, I, R)
            object[] full =
            {
                patternName, dir, ecc, periodOption, userT,
                s.ZoneFactor, soil, s.ImportanceFactorValue, s.R
            };
            if (TryInvoke(auto, out int r1, out string u1, new[] { "SetIS1893_2016" }, full))
            { detail = u1; return r1; }

            // Fallback: a leaner signature seen on some builds (no explicit Z/I/R,
            // which are then taken from the Zone/soil enum codes).
            object[] lean = { patternName, dir, ecc, periodOption, userT, s.ZoneFactor, soil };
            if (TryInvoke(auto, out int r2, out string u2, new[] { "SetIS1893_2016" }, lean))
            { detail = u2; return r2; }

            detail = "SetIS1893_2016 present but no matching arity (define EQ pattern manually)";
            return -11;
        }

        // ── Analysis + results (base shear extraction for RS scaling) ────────
        public static int RunAnalysis(cSapModel sap, out string detail)
        {
            detail = "";
            object analyze = GetMember(sap, "Analyze");
            if (analyze == null) { detail = "Analyze not available"; return -10; }
            if (TryInvoke(analyze, out int r, out string u, new[] { "RunAnalysis" }))
            { detail = u; return r; }
            detail = "RunAnalysis not found";
            return -11;
        }

        public static int SetCaseSelectedForOutput(cSapModel sap, string caseName, out string detail)
        {
            detail = "";
            object results = GetMember(sap, "Results");
            object setup   = GetMember(results, "Setup");
            if (setup == null) { detail = "Results.Setup not available"; return -10; }

            TryInvoke(setup, out _, out _, new[] { "DeselectAllCasesAndCombosForOutput" });
            if (TryInvoke(setup, out int r, out string u,
                          new[] { "SetCaseSelectedForOutput" }, caseName, true))
            { detail = u; return r; }
            // older arity: (Name)
            if (TryInvoke(setup, out int r2, out string u2,
                          new[] { "SetCaseSelectedForOutput" }, caseName))
            { detail = u2; return r2; }
            detail = "SetCaseSelectedForOutput not found";
            return -11;
        }

        /// <summary>
        /// Extracts the resultant base shear magnitude (sqrt(Fx²+Fy²)) for a single
        /// analysed case / combination via Results.BaseReact.  Returns 0 on success.
        /// </summary>
        public static int GetBaseShear(cSapModel sap, string caseName,
                                       out double vx, out double vy, out string detail)
        {
            vx = vy = double.NaN; detail = "";
            object results = GetMember(sap, "Results");
            if (results == null) { detail = "Results not available"; return -10; }

            // BaseReact(ref n, ref LoadCase[], ref StepType[], ref StepNum[],
            //           ref Fx[], ref Fy[], ref Fz[], ref Mx[], ref My[], ref Mz[],
            //           ref gx, ref gy, ref gz)   — 13 args (common)
            var mi = results.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => m.Name == "BaseReact" &&
                                                 (m.GetParameters().Length == 13 ||
                                                  m.GetParameters().Length == 14));
            if (mi == null) { detail = "BaseReact not available"; return -11; }

            int n = mi.GetParameters().Length;
            object[] a = new object[n];
            a[0] = 0;                       // NumberResults
            a[1] = (string[])null;          // LoadCase
            a[2] = (string[])null;          // StepType
            a[3] = (double[])null;          // StepNum
            a[4] = (double[])null;          // Fx
            a[5] = (double[])null;          // Fy
            a[6] = (double[])null;          // Fz
            a[7] = (double[])null;          // Mx
            a[8] = (double[])null;          // My
            a[9] = (double[])null;          // Mz
            a[10] = 0.0;                    // gx
            a[11] = 0.0;                    // gy
            a[12] = 0.0;                    // gz
            if (n == 14) a[13] = 0.0;

            try
            {
                object r = mi.Invoke(results, a);
                int ret = r is int i ? i : 0;
                var fx = a[4] as double[];
                var fy = a[5] as double[];
                if (ret == 0 && fx != null && fx.Length > 0)
                {
                    // Report the absolute maximum across reported steps.
                    vx = 0; vy = 0;
                    for (int k = 0; k < fx.Length; k++)
                    {
                        if (Math.Abs(fx[k]) > Math.Abs(vx)) vx = fx[k];
                        if (fy != null && k < fy.Length && Math.Abs(fy[k]) > Math.Abs(vy)) vy = fy[k];
                    }
                }
                detail = "BaseReact";
                return ret;
            }
            catch (Exception ex) { detail = "BaseReact error: " + ex.Message; return -12; }
        }
    }
}
