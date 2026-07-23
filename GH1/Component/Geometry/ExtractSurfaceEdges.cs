using CommonFunction;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NS_Parrot
{
    public class ExtractSurfaceEdges : GH_Component
    {
        public ExtractSurfaceEdges()
          : base("提取曲面边线", "曲面边线",
              "提取曲面或 Brep 的外边线和内部孔洞边线。输出数据树按输入对象、面和闭环保留层级。",
              "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("曲面", "S", "曲面或修剪曲面。", GH_ParamAccess.list);
            pManager.AddBooleanParameter("合并", "J", "是否合并同一闭环内连续的边线。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("全部边线", "E", "外轮廓和内部孔洞的全部边线。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("外边线", "O", "每个曲面面的外轮廓边线。", GH_ParamAccess.tree);
            pManager.AddCurveParameter("内孔边线", "I", "每个曲面面内部孔洞的边线。", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("数量", "N", "全部输出边线数量。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality()) return;

            var surfaces = new List<Surface>();
            if (!DA.GetDataList(0, surfaces) || surfaces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请至少输入一个曲面或 Brep。");
                return;
            }

            bool join = false;
            DA.GetData(1, ref join);
            double tolerance = RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;
            var allEdges = new DataTree<Curve>();
            var outerEdges = new DataTree<Curve>();
            var innerEdges = new DataTree<Curve>();
            int edgeCount = 0;

            for (int surfaceIndex = 0; surfaceIndex < surfaces.Count; surfaceIndex++)
            {
                Brep brep = surfaces[surfaceIndex]?.ToBrep();
                if (brep == null) continue;

                for (int faceIndex = 0; faceIndex < brep.Faces.Count; faceIndex++)
                {
                    BrepFace face = brep.Faces[faceIndex];
                    for (int loopIndex = 0; loopIndex < face.Loops.Count; loopIndex++)
                    {
                        BrepLoop loop = face.Loops[loopIndex];
                        List<Curve> curves = GetLoopEdges(loop, join, tolerance);
                        if (curves.Count == 0) continue;

                        var path = new GH_Path(surfaceIndex, loopIndex);
                        allEdges.AddRange(curves, path);
                        if (loop.LoopType == BrepLoopType.Outer) outerEdges.AddRange(curves, path);
                        else if (loop.LoopType == BrepLoopType.Inner) innerEdges.AddRange(curves, path);
                        edgeCount += curves.Count;
                    }
                }
            }

            DA.SetDataTree(0, allEdges);
            DA.SetDataTree(1, outerEdges);
            DA.SetDataTree(2, innerEdges);
            DA.SetData(3, edgeCount);
        }

        private static List<Curve> GetLoopEdges(BrepLoop loop, bool join, double tolerance)
        {
            var edges = new List<Curve>();
            foreach (BrepTrim trim in loop.Trims)
            {
                if (trim.Edge != null) edges.Add(trim.Edge.DuplicateCurve());
            }

            if (!join || edges.Count < 2) return edges;
            Curve[] joined = Curve.JoinCurves(edges, tolerance);
            return joined == null ? edges : new List<Curve>(joined);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Sketch");

        public override Guid ComponentGuid => new Guid("D413ABA7-50DC-4C16-8F85-D2B629176E29");
    }
}
