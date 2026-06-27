using System;
using System.Collections.Generic;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    public class EtabsDataExtractor
    {
        private cSapModel _sapModel;

        public EtabsDataExtractor(cSapModel sapModel)
        {
            _sapModel = sapModel;
        }

        /// <summary>
        /// Returns a unit-conversion factor so that raw coordinates and thickness
        /// values from the ETABS API (which are in the model's present units) are
        /// brought into metres (for coordinates/spans) or millimetres (for thickness).
        /// CORRECTION: previously the extractor hard-coded "model is in metres".
        /// Now we query GetPresentUnits() and derive the correct SI factors.
        /// </summary>
        private void GetUnitFactors(out double lenToM, out double thkToMm)
        {
            eUnits u = _sapModel.GetPresentUnits();

            // Determine the length dimension of the model unit system.
            // ETABSv1 eUnits values group as kN_m, kN_mm, kip_in, kip_ft, etc.
            // We only need the length part; cast to int and inspect the name.
            string uName = u.ToString(); // e.g. "kN_m_C", "kN_mm_C", "kip_in_C" …

            if (uName.Contains("_mm_"))
            {
                lenToM  = 0.001;   // mm → m
                thkToMm = 1.0;     // mm → mm (thickness already in mm)
            }
            else if (uName.Contains("_in_"))
            {
                lenToM  = 0.0254;  // inch → m
                thkToMm = 25.4;    // inch → mm
            }
            else if (uName.Contains("_ft_"))
            {
                lenToM  = 0.3048;  // ft → m
                thkToMm = 304.8;   // ft → mm
            }
            else
            {
                // Default: assume metres (covers kN_m_C, kgf_m_C, N_m_C …)
                lenToM  = 1.0;
                thkToMm = 1000.0;  // m → mm (thickness in m from API → mm for SlabData)
            }
        }

        public List<SlabData> ExtractSlabs()
        {
            List<SlabData> slabs = new List<SlabData>();

            int numberNames = 0;
            string[] myNames = null;

            // Get all area objects
            int ret = _sapModel.AreaObj.GetNameList(ref numberNames, ref myNames);
            if (ret != 0 || numberNames == 0 || myNames == null)
                return slabs;

            // CORRECTION: determine the model's current unit system ONCE before
            // the loop so all length and thickness values are converted correctly.
            GetUnitFactors(out double lenToM, out double thkToMm);

            foreach (var name in myNames)
            {
                SlabData slab = new SlabData { Name = name };

                // ── Geometry: Lx / Ly ──────────────────────────────────────────
                int numPoints = 0;
                string[] pointNames = null;
                _sapModel.AreaObj.GetPoints(name, ref numPoints, ref pointNames);

                if (numPoints == 4)
                {
                    double x1 = 0, y1 = 0, z1 = 0;
                    double x2 = 0, y2 = 0, z2 = 0;
                    double x3 = 0, y3 = 0, z3 = 0;

                    _sapModel.PointObj.GetCoordCartesian(pointNames[0], ref x1, ref y1, ref z1);
                    _sapModel.PointObj.GetCoordCartesian(pointNames[1], ref x2, ref y2, ref z2);
                    _sapModel.PointObj.GetCoordCartesian(pointNames[2], ref x3, ref y3, ref z3);

                    // Convert from model units → metres, then → mm for SlabData
                    double dist12_m = Math.Sqrt(Math.Pow((x1 - x2) * lenToM, 2) + Math.Pow((y1 - y2) * lenToM, 2));
                    double dist23_m = Math.Sqrt(Math.Pow((x2 - x3) * lenToM, 2) + Math.Pow((y2 - y3) * lenToM, 2));

                    slab.Lx = Math.Min(dist12_m, dist23_m) * 1000; // mm
                    slab.Ly = Math.Max(dist12_m, dist23_m) * 1000; // mm
                }
                else
                {
                    slab.Lx = 4000; // mm — placeholder for non-rectangular panels
                    slab.Ly = 4000;
                }

                // ── Thickness: read from the assigned section property ──────────
                // CORRECTION: previously thickness was hardcoded to 150 mm.
                // Now we query the section property actually assigned to the area.
                double thickness_mm = 150; // fallback default (mm)
                string propName = "";
                int retProp = _sapModel.AreaObj.GetProperty(name, ref propName);
                if (retProp == 0 && !string.IsNullOrEmpty(propName))
                {
                    eSlabType slabType  = eSlabType.Slab;
                    eShellType shellType = eShellType.ShellThin;
                    string matProp      = "";
                    double thkRaw       = 0;  // in model units
                    int color = 0;
                    string notes = "";
                    string guid = "";
                    int retSlab = _sapModel.PropArea.GetSlab(
                        propName, ref slabType, ref shellType, ref matProp, ref thkRaw, ref color, ref notes, ref guid);

                    if (retSlab == 0 && thkRaw > 0)
                    {
                        // thkRaw is in model units; thkToMm converts to mm
                        thickness_mm = thkRaw * thkToMm;
                        // Cache the material name on SlabData for use when writing back
                        slab.MaterialName = matProp;
                    }
                }
                slab.Thickness = thickness_mm;

                // ── Loads: read UDL patterns assigned to the area ─────────────
                // CORRECTION: previously DL/LL/SDL were hardcoded placeholders.
                // Now we iterate over uniform area loads to populate them.
                // Dead-load patterns containing "DEAD" or "DL" → DeadLoad.
                // Live-load patterns containing "LIVE" or "LL"  → LiveLoad.
                // SDL  patterns containing "SDL" or "FINISH"    → SDL.
                // Unrecognised patterns are added to DeadLoad as a safe fallback.
                double dl = 0, ll = 0, sdl = 0;
                int nLoads = 0;
                string[] areaNames = null;
                string[] loadPats  = null;
                string[] cSysList  = null;
                int[]    dirs      = null;
                double[] vals      = null;
                int retLoad = _sapModel.AreaObj.GetLoadUniform(
                    name, ref nLoads, ref areaNames, ref loadPats, ref cSysList, ref dirs, ref vals);

                if (retLoad == 0 && nLoads > 0)
                {
                    for (int i = 0; i < nLoads; i++)
                    {
                        // Direction 6 = gravity (−Z in ETABS). Convert pressure from
                        // model force/length² to kN/m². The force part of the unit
                        // is always consistent with the length part that lenToM covers,
                        // so divide by lenToM² to get kN/m² when the model uses kN.
                        // For other force units a further factor would be needed; for
                        // typical IS practice (kN, m or mm) this covers the common cases.
                        double kNm2 = Math.Abs(vals[i]) / (lenToM * lenToM);
                        string pat = (loadPats[i] ?? "").ToUpperInvariant();

                        if (pat.Contains("LIVE") || pat.Contains("LL"))
                            ll += kNm2;
                        else if (pat.Contains("SDL") || pat.Contains("FINISH") || pat.Contains("FF"))
                            sdl += kNm2;
                        else
                            dl += kNm2; // DL, DEAD, or any unknown pattern
                    }
                }
                else
                {
                    // Fallback to IS 875 typical values when no loads are defined
                    dl  = 1.0;
                    ll  = 3.0;
                    sdl = 1.5;
                }

                slab.DeadLoad              = dl;
                slab.LiveLoad              = ll;
                slab.SuperimposedDeadLoad  = sdl;

                // ── Slab type: classify by Ly/Lx ──────────────────────────────
                if (slab.Lx > 0 && slab.Ly > 0)
                {
                    slab.Type = slab.Ly / slab.Lx > 2.0
                        ? SlabType.OneWay
                        : SlabType.TwoWay;
                }

                // BoundaryCase defaults to 1 (interior panel). The actual boundary
                // condition cannot be determined from geometry alone in the general
                // case — this requires user input or a separate edge-condition pass.
                slab.BoundaryCase = 1;

                slabs.Add(slab);
            }

            return slabs;
        }
    }
}
