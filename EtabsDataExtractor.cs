using System;
using System.Collections.Generic;
using System.Linq;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// Pulls slab geometry, thickness, loads and material grade from the live
    /// ETABS model and packs everything into a list of SlabData.
    ///
    /// PATCH NOTES (v2):
    ///  • GetUnitFactors now also returns the force unit so pressure can be
    ///    converted to kN/m² regardless of whether the model is in kN, N, kip
    ///    or kgf.
    ///  • Concrete fck is now read from the slab section's assigned material
    ///    via PropMaterial.GetOConcrete. Falls back to user/UI default (25)
    ///    when extraction fails.
    ///  • Self-weight load patterns (those containing "SELF" or "SW") are
    ///    skipped to avoid double counting with the engine-side self-weight
    ///    (D × 25 kN/m³).
    ///  • Hardened against null/short pointName arrays.
    /// </summary>
    public class EtabsDataExtractor
    {
        private readonly cSapModel _sapModel;

        // ── User design inputs from the UI (applied to every extracted slab) ──
        public double DefaultFck       { get; set; } = 25;   // used if material lookup fails
        public double UserFy           { get; set; } = 500;
        public double UserCover        { get; set; } = 20;
        public double UserBarDiaMain   { get; set; } = 10;
        public double UserBarDiaDist   { get; set; } = 8;

        public EtabsDataExtractor(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        /// <summary>
        /// Returns conversion factors so raw ETABS values reach the SI units
        /// SlabData expects (lengths in mm, pressures in kN/m²).
        ///
        ///   lenToM      : multiply a length in model units by this to get metres
        ///   thkToMm     : multiply a thickness in model units by this to get mm
        ///   forceToKN   : multiply a force in model units by this to get kN
        /// </summary>
        private void GetUnitFactors(out double lenToM, out double thkToMm, out double forceToKN)
        {
            eUnits u = _sapModel.GetPresentUnits();
            string uName = u.ToString();   // e.g. "kN_m_C", "kip_in_C", "kgf_mm_C"

            // ── Length component ──
            if      (uName.Contains("_mm_")) { lenToM = 0.001;  thkToMm = 1.0;     }
            else if (uName.Contains("_in_")) { lenToM = 0.0254; thkToMm = 25.4;    }
            else if (uName.Contains("_ft_")) { lenToM = 0.3048; thkToMm = 304.8;   }
            else                              { lenToM = 1.0;    thkToMm = 1000.0; } // metres

            // ── Force component ──
            if      (uName.StartsWith("kN_"))  forceToKN = 1.0;
            else if (uName.StartsWith("N_"))   forceToKN = 0.001;
            else if (uName.StartsWith("kip_")) forceToKN = 4.4482216;
            else if (uName.StartsWith("lb_"))  forceToKN = 0.0044482;
            else if (uName.StartsWith("kgf_")) forceToKN = 0.00980665;
            else if (uName.StartsWith("tonf_"))forceToKN = 9.80665;
            else                                forceToKN = 1.0; // assume kN
        }

        public List<SlabData> ExtractSlabs()
        {
            var slabs = new List<SlabData>();

            int numberNames = 0;
            string[] myNames = null;
            int ret = _sapModel.AreaObj.GetNameList(ref numberNames, ref myNames);
            if (ret != 0 || numberNames == 0 || myNames == null) return slabs;

            GetUnitFactors(out double lenToM, out double thkToMm, out double forceToKN);

            // Cache fck per concrete material so we don't query the API repeatedly.
            var fckCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var name in myNames)
            {
                var slab = new SlabData
                {
                    Name       = name,
                    Fy         = UserFy,
                    Cover      = UserCover,
                    BarDiaMain = UserBarDiaMain,
                    BarDiaDist = UserBarDiaDist,
                    Fck        = DefaultFck      // overwritten below if material lookup succeeds
                };

                // ── Geometry: Lx / Ly ─────────────────────────────────────
                int numPoints = 0;
                string[] pointNames = null;
                _sapModel.AreaObj.GetPoints(name, ref numPoints, ref pointNames);

                if (numPoints == 4 && pointNames != null && pointNames.Length >= 3)
                {
                    double x1=0, y1=0, z1=0, x2=0, y2=0, z2=0, x3=0, y3=0, z3=0;
                    _sapModel.PointObj.GetCoordCartesian(pointNames[0], ref x1, ref y1, ref z1);
                    _sapModel.PointObj.GetCoordCartesian(pointNames[1], ref x2, ref y2, ref z2);
                    _sapModel.PointObj.GetCoordCartesian(pointNames[2], ref x3, ref y3, ref z3);

                    double d12 = Math.Sqrt(Math.Pow((x1-x2)*lenToM,2) + Math.Pow((y1-y2)*lenToM,2));
                    double d23 = Math.Sqrt(Math.Pow((x2-x3)*lenToM,2) + Math.Pow((y2-y3)*lenToM,2));
                    slab.Lx = Math.Min(d12, d23) * 1000; // → mm
                    slab.Ly = Math.Max(d12, d23) * 1000; // → mm
                }
                else if (numPoints >= 3 && pointNames != null)
                {
                    // PATCH v3: non-quadrilateral panel — use bounding-box of
                    // all vertices so the geometry is at least representative.
                    double minX=double.MaxValue, maxX=double.MinValue;
                    double minY=double.MaxValue, maxY=double.MinValue;
                    for (int i = 0; i < numPoints; i++)
                    {
                        double xp=0, yp=0, zp=0;
                        _sapModel.PointObj.GetCoordCartesian(pointNames[i], ref xp, ref yp, ref zp);
                        minX = Math.Min(minX, xp); maxX = Math.Max(maxX, xp);
                        minY = Math.Min(minY, yp); maxY = Math.Max(maxY, yp);
                    }
                    double bx = (maxX - minX) * lenToM * 1000.0;
                    double by = (maxY - minY) * lenToM * 1000.0;
                    slab.Lx = Math.Min(bx, by);
                    slab.Ly = Math.Max(bx, by);
                    slab.Type = SlabType.Unknown;       // flag for review
                }
                else
                {
                    // PATCH v3: do NOT silently substitute 4 m × 4 m. Flag for
                    // user attention instead — designing with fake geometry
                    // gives misleading results.
                    slab.Lx = 0; slab.Ly = 0;
                    slab.Type = SlabType.Unknown;
                    slab.DesignStatus = "SKIPPED (geometry not extractable)";
                    slab.Notes = $"Panel has {numPoints} vertices — could not extract a usable Lx/Ly. " +
                                 "Inspect the area object in ETABS.";
                }

                // ── Thickness & material (from the assigned section property) ──
                double thickness_mm = 150;
                string propName = "";
                int retProp = _sapModel.AreaObj.GetProperty(name, ref propName);
                if (retProp == 0 && !string.IsNullOrEmpty(propName))
                {
                    eSlabType slabType   = eSlabType.Slab;
                    eShellType shellType = eShellType.ShellThin;
                    string matProp = "";
                    double thkRaw  = 0;
                    int color = 0;
                    string notes = "", guid = "";

                    int retSlab = _sapModel.PropArea.GetSlab(
                        propName, ref slabType, ref shellType, ref matProp,
                        ref thkRaw, ref color, ref notes, ref guid);

                    if (retSlab == 0 && thkRaw > 0)
                    {
                        thickness_mm = thkRaw * thkToMm;
                        slab.MaterialName = matProp ?? "";

                        // PATCH: pull fck from the concrete material itself.
                        if (!string.IsNullOrEmpty(matProp))
                        {
                            if (!fckCache.TryGetValue(matProp, out double fck))
                            {
                                fck = TryReadFck(matProp);
                                if (fck > 0) fckCache[matProp] = fck;
                            }
                            if (fck > 0) slab.Fck = fck;
                        }
                    }
                }
                slab.Thickness = thickness_mm;

                // ── Loads: UDLs assigned to the area object ───────────────
                double dl = 0, ll = 0, sdl = 0;
                int nLoads = 0;
                string[] areaNames = null, loadPats = null, cSysList = null;
                int[]    dirs = null;
                double[] vals = null;

                int retLoad = _sapModel.AreaObj.GetLoadUniform(
                    name, ref nLoads, ref areaNames, ref loadPats,
                    ref cSysList, ref dirs, ref vals);

                if (retLoad == 0 && nLoads > 0 && vals != null && loadPats != null)
                {
                    for (int i = 0; i < nLoads; i++)
                    {
                        // Convert pressure: model is [force/length²]. We want kN/m².
                        //   forceToKN  brings the numerator to kN
                        //   1/lenToM²  brings the denominator from model length² to m²
                        double kNm2 = Math.Abs(vals[i]) * forceToKN / (lenToM * lenToM);
                        string pat  = (loadPats[i] ?? "").ToUpperInvariant();

                        // PATCH: ignore self-weight patterns — we add D·25 ourselves
                        // in the design engine.  This prevents a 2× self-weight error
                        // when the user has applied a non-zero Self-Weight Multiplier
                        // and ETABS surfaces it as a uniform area pressure.
                        if (pat.Contains("SELF") || pat == "SW") continue;

                        if      (pat.Contains("LIVE")   || pat.Contains("LL")) ll  += kNm2;
                        else if (pat.Contains("SDL")    || pat.Contains("FINISH")
                                 || pat.Contains("FF")  || pat.Contains("PART"))  sdl += kNm2;
                        else                                                       dl  += kNm2;
                    }
                }
                else
                {
                    // No UDL defined → IS 875 typical residential loading
                    dl  = 1.0;
                    ll  = 3.0;
                    sdl = 1.5;
                }

                slab.DeadLoad             = dl;
                slab.LiveLoad             = ll;
                slab.SuperimposedDeadLoad = sdl;

                // ── Preliminary type classification by Ly/Lx ──────────────
                if (slab.Lx > 0 && slab.Ly > 0 && slab.Type != SlabType.Unknown)
                    slab.Type = slab.Ly / slab.Lx > 2.0 ? SlabType.OneWay : SlabType.TwoWay;

                slabs.Add(slab);
            }

            // ── Topology pass: support widths, continuity, flat-slab detect ──
            var analyzer = new GeometricTopologyAnalyzer(_sapModel, lenToM);
            analyzer.AnalyzeSlabs(slabs);

            // ── Extract column geometry & drop panels for flat slabs ──────
            ExtractFlatSlabProperties(slabs, lenToM);

            return slabs;
        }

        /// <summary>
        /// Pulls fck (in N/mm²) from a concrete material name. Returns 0 if the
        /// material is not concrete, the API call fails, or the value is invalid.
        /// </summary>
        private double TryReadFck(string matName)
        {
            try
            {
                eMatType matType = eMatType.Concrete;
                int symType = 0;
                int ret = _sapModel.PropMaterial.GetTypeOAPI(matName, ref matType, ref symType);
                if (ret != 0 || matType != eMatType.Concrete) return 0;

                double fc=0;
                bool isLightweight = false;
                double lightWeightFactor = 0;
                int sstype = 0, sshyster = 0;
                double strainAtPeakStress = 0, strainAtUltStress = 0, finalSlope = 0;
                double frictionAngle = 0, dilationalAngle = 0;

                int r2 = _sapModel.PropMaterial.GetOConcrete_1(
                    matName, ref fc, ref isLightweight, ref lightWeightFactor,
                    ref sstype, ref sshyster, ref strainAtPeakStress,
                    ref strainAtUltStress, ref finalSlope, ref frictionAngle, ref dilationalAngle);

                if (r2 == 0 && fc > 0)
                {
                    // fc is in model stress units; we need N/mm² (MPa).
                    // ETABS stress = force/length². Reuse our unit factors.
                    GetUnitFactors(out double lenToM, out _, out double forceToKN);
                    // forceToKN converts force → kN; we need force → N (×1000).
                    // 1/lenToM² converts length² → m²; we need length² → mm² (×1e-6).
                    double fc_Nmm2 = fc * (forceToKN * 1000.0) / (lenToM * lenToM) * 1e-6;
                    return fc_Nmm2;
                }
            }
            catch { /* fall through */ }
            return 0;
        }

        private void ExtractFlatSlabProperties(List<SlabData> slabs, double lenToM)
        {
            // 1) Map vertical frames (columns) to their top points + dimensions
            int numFrames = 0;
            string[] frameNames = null;
            _sapModel.FrameObj.GetNameList(ref numFrames, ref frameNames);

            var columnTopPoints = new Dictionary<string, (double t3, double t2)>();

            if (numFrames > 0 && frameNames != null)
            {
                foreach (var fname in frameNames)
                {
                    string p1 = "", p2 = "";
                    _sapModel.FrameObj.GetPoints(fname, ref p1, ref p2);

                    double x1=0, y1=0, z1=0, x2=0, y2=0, z2=0;
                    _sapModel.PointObj.GetCoordCartesian(p1, ref x1, ref y1, ref z1);
                    _sapModel.PointObj.GetCoordCartesian(p2, ref x2, ref y2, ref z2);

                    bool isVertical = Math.Abs(z1 - z2) > 0.1 &&
                                      Math.Abs(x1 - x2) < 0.1 &&
                                      Math.Abs(y1 - y2) < 0.1;
                    if (!isVertical) continue;

                    string propName = "", sAuto = "";
                    _sapModel.FrameObj.GetSection(fname, ref propName, ref sAuto);

                    string fileName="", matProp="", notes="", guid="";
                    double t3=0, t2=0; int color=0;

                    int ret = _sapModel.PropFrame.GetRectangle(
                        propName, ref fileName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid);
                    if (ret != 0) continue;

                    string topPoint = z1 > z2 ? p1 : p2;
                    columnTopPoints[topPoint] = (t3 * lenToM * 1000.0, t2 * lenToM * 1000.0);
                }
            }

            // 2) For each flat-slab panel, find columns at its corners + any drop panel
            foreach (var slab in slabs.Where(s => s.Type == SlabType.FlatSlab))
            {
                int numPoints = 0;
                string[] pointNames = null;
                _sapModel.AreaObj.GetPoints(slab.Name, ref numPoints, ref pointNames);

                double max_t3 = 400, max_t2 = 400;
                bool foundCol = false;

                if (numPoints > 0 && pointNames != null)
                {
                    foreach (var pt in pointNames)
                    {
                        if (columnTopPoints.TryGetValue(pt, out var dim))
                        {
                            max_t3 = Math.Max(max_t3, dim.t3);
                            max_t2 = Math.Max(max_t2, dim.t2);
                            foundCol = true;
                        }
                    }
                }

                slab.HasDrop = false;
                if (!foundCol) continue;

                slab.c1 = max_t3;
                slab.c2 = max_t2;

                int numTotalAreas = 0;
                string[] allAreaNames = null;
                _sapModel.AreaObj.GetNameList(ref numTotalAreas, ref allAreaNames);
                if (numTotalAreas == 0 || allAreaNames == null) continue;

                foreach (var aName in allAreaNames)
                {
                    if (aName == slab.Name) continue;

                    int nPts = 0;
                    string[] pts = null;
                    _sapModel.AreaObj.GetPoints(aName, ref nPts, ref pts);
                    if (pts == null || nPts == 0) continue;

                    bool sharesColPoint = false;
                    foreach (var p in pts)
                    {
                        if (pointNames != null && pointNames.Contains(p) && columnTopPoints.ContainsKey(p))
                        {
                            sharesColPoint = true;
                            break;
                        }
                    }
                    if (!sharesColPoint) continue;

                    string propName2 = "";
                    _sapModel.AreaObj.GetProperty(aName, ref propName2);

                    eSlabType sType = eSlabType.Slab;
                    eShellType shType = eShellType.ShellThin;
                    string mat="", notes2="", guid2="";
                    double thickness = 0;
                    int color = 0;

                    int ret2 = _sapModel.PropArea.GetSlab(propName2,
                        ref sType, ref shType, ref mat, ref thickness, ref color, ref notes2, ref guid2);
                    if (ret2 != 0) continue;

                    double dropD_mm = thickness * lenToM * 1000.0;
                    if (dropD_mm > slab.Thickness)
                    {
                        // Bounding box of the drop area
                        double minX=double.MaxValue, maxX=double.MinValue;
                        double minY=double.MaxValue, maxY=double.MinValue;
                        foreach (var p in pts)
                        {
                            double x=0, y=0, z=0;
                            _sapModel.PointObj.GetCoordCartesian(p, ref x, ref y, ref z);
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        }

                        slab.HasDrop  = true;
                        slab.DropDepth = dropD_mm;
                        slab.DropL1   = (maxX - minX) * lenToM * 1000.0;
                        slab.DropL2   = (maxY - minY) * lenToM * 1000.0;
                        break;
                    }
                }
            }
        }
    }
}
