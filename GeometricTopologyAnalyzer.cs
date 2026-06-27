using System;
using System.Collections.Generic;
using System.Linq;
using ETABSv1;

namespace CSiNET8PluginExample1
{
    public class GeometricTopologyAnalyzer
    {
        private readonly cSapModel _sapModel;
        private double _lenToM;

        public GeometricTopologyAnalyzer(cSapModel sapModel, double lenToM)
        {
            _sapModel = sapModel;
            _lenToM = lenToM;
        }

        public class Point3D
        {
            public double X, Y, Z;
            public Point3D(double x, double y, double z) { X = Math.Round(x, 3); Y = Math.Round(y, 3); Z = Math.Round(z, 3); }

            public override bool Equals(object obj)
            {
                if (obj is Point3D p) return X == p.X && Y == p.Y && Z == p.Z;
                return false;
            }
            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        public class Edge
        {
            public Point3D P1, P2;
            public double Length;

            public Edge(Point3D p1, Point3D p2)
            {
                // Sort points so Edge(A,B) == Edge(B,A)
                if (p1.X < p2.X || (p1.X == p2.X && p1.Y < p2.Y))
                {
                    P1 = p1; P2 = p2;
                }
                else
                {
                    P1 = p2; P2 = p1;
                }
                Length = Math.Sqrt(Math.Pow(P1.X - P2.X, 2) + Math.Pow(P1.Y - P2.Y, 2));
            }

            public override bool Equals(object obj)
            {
                if (obj is Edge e) return P1.Equals(e.P1) && P2.Equals(e.P2);
                return false;
            }
            public override int GetHashCode() => HashCode.Combine(P1, P2);
        }

        public void AnalyzeSlabs(List<SlabData> slabs)
        {
            var edgeToSlabs = new Dictionary<Edge, List<SlabData>>();
            var slabEdges = new Dictionary<SlabData, List<Edge>>();

            // 1. Map all slabs to their edges
            foreach (var slab in slabs)
            {
                int numPoints = 0;
                string[] pointNames = null;
                _sapModel.AreaObj.GetPoints(slab.Name, ref numPoints, ref pointNames);

                var edges = new List<Edge>();
                if (numPoints >= 3)
                {
                    var points = new List<Point3D>();
                    for (int i = 0; i < numPoints; i++)
                    {
                        double x = 0, y = 0, z = 0;
                        _sapModel.PointObj.GetCoordCartesian(pointNames[i], ref x, ref y, ref z);
                        points.Add(new Point3D(x * _lenToM, y * _lenToM, z * _lenToM));
                    }

                    for (int i = 0; i < numPoints; i++)
                    {
                        var edge = new Edge(points[i], points[(i + 1) % numPoints]);
                        edges.Add(edge);

                        if (!edgeToSlabs.ContainsKey(edge))
                            edgeToSlabs[edge] = new List<SlabData>();
                        edgeToSlabs[edge].Add(slab);
                    }
                }
                slabEdges[slab] = edges;
            }

            // 2. Map all beams (Frame Objects)
            int numFrames = 0;
            string[] frameNames = null;
            _sapModel.FrameObj.GetNameList(ref numFrames, ref frameNames);
            var beamEdges = new Dictionary<Edge, double>();
            
            if (numFrames > 0 && frameNames != null)
            {
                foreach (var fname in frameNames)
                {
                    string p1 = "", p2 = "";
                    _sapModel.FrameObj.GetPoints(fname, ref p1, ref p2);
                    double x1=0, y1=0, z1=0, x2=0, y2=0, z2=0;
                    _sapModel.PointObj.GetCoordCartesian(p1, ref x1, ref y1, ref z1);
                    _sapModel.PointObj.GetCoordCartesian(p2, ref x2, ref y2, ref z2);
                    
                    // Only consider horizontal frames as beams
                    if (Math.Abs(z1 - z2) < 0.1)
                    {
                        string propName = "", sAuto = "";
                        _sapModel.FrameObj.GetSection(fname, ref propName, ref sAuto);
                        
                        string fileName="", matProp="", notes="", guid="";
                        double t3=0, t2=0; int color=0;
                        int ret = _sapModel.PropFrame.GetRectangle(propName, ref fileName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid);
                        double width = ret == 0 ? (t2 * _lenToM * 1000.0) : 230.0; // default 230mm if not rectangular

                        var edge = new Edge(
                            new Point3D(x1 * _lenToM, y1 * _lenToM, z1 * _lenToM),
                            new Point3D(x2 * _lenToM, y2 * _lenToM, z2 * _lenToM)
                        );
                        beamEdges[edge] = width;
                    }
                }
            }

            // 3. Classify Each Slab
            foreach (var slab in slabs)
            {
                var edges = slabEdges[slab];
                if (edges.Count == 4) // Rectangular IS 456 Table 26 logic
                {
                    // Sort edges by length to separate Short and Long
                    var sortedEdges = edges.OrderBy(e => e.Length).ToList();
                    var shortEdges = new List<Edge> { sortedEdges[0], sortedEdges[1] };
                    var longEdges = new List<Edge> { sortedEdges[2], sortedEdges[3] };

                    int shortDisc = shortEdges.Count(e => edgeToSlabs[e].Count == 1);
                    int longDisc = longEdges.Count(e => edgeToSlabs[e].Count == 1);
                    
                    // Edges bounding Lx (short span) have length Ly (long edges).
                    slab.SupportWidthX1 = beamEdges.ContainsKey(longEdges[0]) ? beamEdges[longEdges[0]] : 0;
                    slab.SupportWidthX2 = beamEdges.ContainsKey(longEdges[1]) ? beamEdges[longEdges[1]] : 0;
                    slab.IsContinuousX1 = edgeToSlabs[longEdges[0]].Count > 1;
                    slab.IsContinuousX2 = edgeToSlabs[longEdges[1]].Count > 1;

                    // Edges bounding Ly (long span) have length Lx (short edges).
                    slab.SupportWidthY1 = beamEdges.ContainsKey(shortEdges[0]) ? beamEdges[shortEdges[0]] : 0;
                    slab.SupportWidthY2 = beamEdges.ContainsKey(shortEdges[1]) ? beamEdges[shortEdges[1]] : 0;
                    slab.IsContinuousY1 = edgeToSlabs[shortEdges[0]].Count > 1;
                    slab.IsContinuousY2 = edgeToSlabs[shortEdges[1]].Count > 1;

                    // Beam check for Flat Slab
                    int edgesWithBeams = edges.Count(e => beamEdges.ContainsKey(e));
                    if (edgesWithBeams == 0 && edgeToSlabs.Values.Any(list => list.Count > 1))
                    {
                        slab.Type = SlabType.FlatSlab;
                    }
                    else if (edges.Count(e => edgeToSlabs[e].Count > 1) == 1) // Only 1 continuous edge
                    {
                        slab.Type = SlabType.Cantilever;
                    }

                    // Assign IS 456 Boundary Case
                    if (shortDisc == 0 && longDisc == 0) slab.BoundaryCase = 1; // 4 cont
                    else if (shortDisc == 1 && longDisc == 0) slab.BoundaryCase = 2; // 1 short disc
                    else if (shortDisc == 0 && longDisc == 1) slab.BoundaryCase = 3; // 1 long disc
                    else if (shortDisc == 1 && longDisc == 1) slab.BoundaryCase = 4; // 2 adj disc (approx, assume adjacent)
                    else if (shortDisc == 2 && longDisc == 0) slab.BoundaryCase = 5; // 2 short disc
                    else if (shortDisc == 0 && longDisc == 2) slab.BoundaryCase = 6; // 2 long disc
                    else if (shortDisc == 2 && longDisc == 1) slab.BoundaryCase = 7; // 3 disc (1 long cont)
                    else if (shortDisc == 1 && longDisc == 2) slab.BoundaryCase = 8; // 3 disc (1 short cont)
                    else if (shortDisc == 2 && longDisc == 2) slab.BoundaryCase = 9; // 4 disc
                }
                else
                {
                    // Default for non-rectangular
                    slab.BoundaryCase = 9; // Safest case (Simply supported)
                }
            }
        }
    }
}
