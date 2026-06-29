using System;
using System.Collections.Generic;
using System.Linq;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    /// <summary>
    /// Inspects the slab/beam/column topology of the model to populate every
    /// slab's continuity flags, support widths, IS 456 Table-26 boundary case,
    /// and (optionally) reclassify TwoWay slabs as FlatSlab or Cantilever.
    ///
    /// PATCH NOTES (v2):
    ///  • Zero-length edges (produced by coincident vertices in ETABS) are now
    ///    rejected so they don't poison the edge dictionary.
    ///  • The FlatSlab detection rule now also requires that at least one
    ///    corner of the panel sits on a column (i.e. the panel rests on
    ///    columns directly, not on beams).  This prevents misclassifying a
    ///    plain isolated TwoWay panel as a flat slab.
    ///  • The Cantilever detection rule requires exactly one continuous edge
    ///    AND that the support widths along that edge are non-zero (i.e. it's
    ///    actually a beam, not a free edge).
    /// </summary>
    public class GeometricTopologyAnalyzer
    {
        private readonly cSapModel _sapModel;
        private readonly double _lenToM;

        public GeometricTopologyAnalyzer(cSapModel sapModel, double lenToM)
        {
            _sapModel = sapModel;
            _lenToM = lenToM;
        }

        public class Point3D
        {
            public double X, Y, Z;
            public Point3D(double x, double y, double z)
            { X = Math.Round(x, 3); Y = Math.Round(y, 3); Z = Math.Round(z, 3); }

            public override bool Equals(object? obj)
                => obj is Point3D p && X == p.X && Y == p.Y && Z == p.Z;
            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        public class Edge
        {
            public Point3D P1, P2;
            public double  Length;

            public Edge(Point3D p1, Point3D p2)
            {
                // Canonicalise endpoint order so Edge(A,B) == Edge(B,A)
                if (p1.X < p2.X
                    || (p1.X == p2.X && p1.Y <  p2.Y)
                    || (p1.X == p2.X && p1.Y == p2.Y && p1.Z < p2.Z))
                { P1 = p1; P2 = p2; }
                else
                { P1 = p2; P2 = p1; }

                Length = Math.Sqrt(
                    Math.Pow(P1.X - P2.X, 2) +
                    Math.Pow(P1.Y - P2.Y, 2) +
                    Math.Pow(P1.Z - P2.Z, 2));
            }

            public override bool Equals(object? obj)
                => obj is Edge e && P1.Equals(e.P1) && P2.Equals(e.P2);
            public override int GetHashCode() => HashCode.Combine(P1, P2);
        }

        public void AnalyzeSlabs(List<SlabData> slabs)
        {
            var edgeToSlabs = new Dictionary<Edge, List<SlabData>>();
            var slabEdges   = new Dictionary<SlabData, List<Edge>>();

            // ── 1. Map every slab to its edges ────────────────────────────
            foreach (var slab in slabs)
            {
                int numPoints = 0;
                string[] pointNames = null;
                _sapModel.AreaObj.GetPoints(slab.Name, ref numPoints, ref pointNames);

                var edges = new List<Edge>();
                if (numPoints >= 3 && pointNames != null)
                {
                    var points = new List<Point3D>();
                    for (int i = 0; i < numPoints; i++)
                    {
                        double x=0, y=0, z=0;
                        _sapModel.PointObj.GetCoordCartesian(pointNames[i], ref x, ref y, ref z);
                        points.Add(new Point3D(x * _lenToM, y * _lenToM, z * _lenToM));
                    }
                    for (int i = 0; i < numPoints; i++)
                    {
                        var edge = new Edge(points[i], points[(i + 1) % numPoints]);
                        if (edge.Length < 1e-4) continue;     // PATCH: skip degenerate edges
                        edges.Add(edge);

                        if (!edgeToSlabs.ContainsKey(edge))
                            edgeToSlabs[edge] = new List<SlabData>();
                        edgeToSlabs[edge].Add(slab);
                    }
                }
                slabEdges[slab] = edges;
            }

            // ── 2. Map every horizontal beam to its edge + width ──────────
            int numFrames = 0;
            string[] frameNames = null;
            _sapModel.FrameObj.GetNameList(ref numFrames, ref frameNames);
            var beamEdges = new Dictionary<Edge, double>();

            // Track which slab corner points are column tops (for FlatSlab detection)
            var columnTopPoints = new HashSet<Point3D>();

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
                    bool isHorizontal = Math.Abs(z1 - z2) < 0.1;

                    if (isVertical)
                    {
                        // PATCH (fix #1, latent): use the X/Y of whichever end
                        // is actually on top, not always (x1,y1).  For a
                        // perfectly vertical column the two ends share X/Y so
                        // the old code accidentally worked, but if the column
                        // is tagged with p2 on top — or has any small lean —
                        // the column-top point is wrong and the FlatSlab
                        // detector misses panels that genuinely rest on it.
                        bool p1OnTop = z1 >= z2;
                        double topX = p1OnTop ? x1 : x2;
                        double topY = p1OnTop ? y1 : y2;
                        double topZ = p1OnTop ? z1 : z2;
                        columnTopPoints.Add(new Point3D(topX * _lenToM, topY * _lenToM, topZ * _lenToM));
                    }
                    else if (isHorizontal)
                    {
                        string propName = "", sAuto = "";
                        _sapModel.FrameObj.GetSection(fname, ref propName, ref sAuto);
                        string fileName="", matProp="", notes="", guid="";
                        double t3=0, t2=0; int color=0;
                        int ret = _sapModel.PropFrame.GetRectangle(propName,
                            ref fileName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid);
                        double width = ret == 0 ? (t2 * _lenToM * 1000.0) : 230.0;

                        var edge = new Edge(
                            new Point3D(x1 * _lenToM, y1 * _lenToM, z1 * _lenToM),
                            new Point3D(x2 * _lenToM, y2 * _lenToM, z2 * _lenToM));
                        if (edge.Length > 1e-4)
                            beamEdges[edge] = width;
                    }
                }
            }

            // ── 3. Classify each slab ─────────────────────────────────────
            foreach (var slab in slabs)
            {
                var edges = slabEdges[slab];
                if (edges.Count != 4)
                {
                    slab.BoundaryCase = 9; // safest (all simply supported)
                    continue;
                }

                var sorted     = edges.OrderBy(e => e.Length).ToList();
                var shortEdges = new[] { sorted[0], sorted[1] };
                var longEdges  = new[] { sorted[2], sorted[3] };

                int shortDisc = shortEdges.Count(e => edgeToSlabs[e].Count == 1);
                int longDisc  = longEdges .Count(e => edgeToSlabs[e].Count == 1);

                slab.SupportWidthX1 = beamEdges.TryGetValue(longEdges[0],  out double w1) ? w1 : 0;
                slab.SupportWidthX2 = beamEdges.TryGetValue(longEdges[1],  out double w2) ? w2 : 0;
                slab.IsContinuousX1 = edgeToSlabs[longEdges[0]].Count > 1;
                slab.IsContinuousX2 = edgeToSlabs[longEdges[1]].Count > 1;

                slab.SupportWidthY1 = beamEdges.TryGetValue(shortEdges[0], out double w3) ? w3 : 0;
                slab.SupportWidthY2 = beamEdges.TryGetValue(shortEdges[1], out double w4) ? w4 : 0;
                slab.IsContinuousY1 = edgeToSlabs[shortEdges[0]].Count > 1;
                slab.IsContinuousY2 = edgeToSlabs[shortEdges[1]].Count > 1;

                // PATCH (fix #4): flag edge / corner panels.  A panel is an "end
                // panel" for DDM purposes whenever ANY of its four edges is
                // discontinuous (i.e. not shared with another slab — it sits
                // on the model boundary).  This covers edge panels (one free
                // edge) and corner panels (two free edges).  FlatSlabEngine
                // uses this to swap the interior long-span split (-0.65 /
                // +0.35 M₀) for the end-span split (-0.75 / +0.50 M₀)
                // mandated by IS 456 Cl. 31.4.3.
                slab.IsEndPanel = (shortDisc + longDisc) > 0;

                int edgesWithBeams = edges.Count(e => beamEdges.ContainsKey(e));

                // PATCH: flat-slab needs (a) no edge beams AND (b) at least one
                // corner of the panel actually resting on a column.
                if (edgesWithBeams == 0 && AnyCornerOnColumn(slab, columnTopPoints))
                {
                    slab.Type = SlabType.FlatSlab;
                }
                else if (edges.Count(e => edgeToSlabs[e].Count > 1) == 1)
                {
                    // PATCH: also require the single continuous edge to be
                    // backed by a beam (otherwise it's just floating)
                    bool hasBeam =
                        (slab.SupportWidthX1 > 0 && slab.IsContinuousX1) ||
                        (slab.SupportWidthX2 > 0 && slab.IsContinuousX2) ||
                        (slab.SupportWidthY1 > 0 && slab.IsContinuousY1) ||
                        (slab.SupportWidthY2 > 0 && slab.IsContinuousY2);
                    if (hasBeam) slab.Type = SlabType.Cantilever;
                }

                // IS 456 Table 26 boundary case (1–9).  Only meaningful for
                // beam-supported TwoWay panels, but we set it anyway for
                // diagnostic display.
                if      (shortDisc == 0 && longDisc == 0) slab.BoundaryCase = 1;
                else if (shortDisc == 1 && longDisc == 0) slab.BoundaryCase = 2;
                else if (shortDisc == 0 && longDisc == 1) slab.BoundaryCase = 3;
                else if (shortDisc == 1 && longDisc == 1) slab.BoundaryCase = 4;
                else if (shortDisc == 2 && longDisc == 0) slab.BoundaryCase = 5;
                else if (shortDisc == 0 && longDisc == 2) slab.BoundaryCase = 6;
                else if (shortDisc == 2 && longDisc == 1) slab.BoundaryCase = 7;
                else if (shortDisc == 1 && longDisc == 2) slab.BoundaryCase = 8;
                else if (shortDisc == 2 && longDisc == 2) slab.BoundaryCase = 9;
            }
        }

        private bool AnyCornerOnColumn(SlabData slab, HashSet<Point3D> columnTops)
        {
            int numPoints = 0;
            string[] pointNames = null;
            _sapModel.AreaObj.GetPoints(slab.Name, ref numPoints, ref pointNames);
            if (numPoints == 0 || pointNames == null) return false;

            foreach (var pName in pointNames)
            {
                double x=0, y=0, z=0;
                _sapModel.PointObj.GetCoordCartesian(pName, ref x, ref y, ref z);
                var p = new Point3D(x * _lenToM, y * _lenToM, z * _lenToM);
                if (columnTops.Contains(p)) return true;
            }
            return false;
        }
    }
}
