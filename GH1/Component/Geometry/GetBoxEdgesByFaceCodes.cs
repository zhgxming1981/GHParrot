using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace NS_Parrot
{
    /// <summary>
    /// 根据面代号取得 Box 的十二条棱线。
    /// </summary>
    public class GetBoxEdgesByFaceCodes : GH_Component
    {
        private const double FrontAngleToleranceDegrees = 30.0;

        public GetBoxEdgesByFaceCodes()
            : base("Box棱线", "Box棱线",
                "根据前方向和面代号输出 Box 的棱线。支持全部 12 条棱线，例如：前左、后上、左下、右上；面代号顺序不限。",
                "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("盒子", "Box", "需要提取棱线的 Box。", GH_ParamAccess.item);
            pManager.AddVectorParameter("前方向", "V", "指向前方的向量；仅使用其 XY 方向。", GH_ParamAccess.item);
            pManager.AddTextParameter("面代号", "面", "面代号列表，例如：前左、左前、后上、左下。", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("棱线", "棱线", "与输入面代号一一对应的 Box 棱线。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = new Box();
            Vector3d forward = Vector3d.Unset;
            List<string> faceCodes = new List<string>();

            if (!DA.GetData(0, ref box) || !DA.GetData(1, ref forward) || !DA.GetDataList(2, faceCodes))
                return;

            if (!box.IsValid)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "输入的 Box 无效。");
                return;
            }

            forward.Z = 0.0;
            if (!forward.Unitize())
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "前方向向量在 XY 平面上的投影不能为零。");
                return;
            }

            Brep boxBrep = box.ToBrep();
            if (boxBrep == null || boxBrep.Faces.Count != 6)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "无法从输入 Box 获取六个面。");
                return;
            }

            Dictionary<string, FaceInfo> faceMap;
            if (!TryCreateFaceMap(boxBrep, forward, out faceMap))
                return;

            List<Curve> edges = new List<Curve>();
            for (int i = 0; i < faceCodes.Count; i++)
            {
                string code = NormalizeFaceCode(faceCodes[i]);
                string firstFace;
                string secondFace;
                if (!TryParseFaceCode(code, out firstFace, out secondFace))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"第 {i + 1} 个面代号“{faceCodes[i]}”无效，已跳过。");
                    continue;
                }

                Curve edge = FindSharedEdge(boxBrep, faceMap[firstFace].Index, faceMap[secondFace].Index);
                if (edge == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"未找到“{faceCodes[i]}”对应的棱线。");
                    continue;
                }

                edges.Add(edge);
            }

            DA.SetDataList(0, edges);
        }

        private bool TryCreateFaceMap(Brep boxBrep, Vector3d forward, out Dictionary<string, FaceInfo> faceMap)
        {
            List<FaceInfo> faces = GetFaces(boxBrep);
            faces.Sort((a, b) => a.Center.Z.CompareTo(b.Center.Z));

            FaceInfo bottom = faces[0];
            FaceInfo top = faces[faces.Count - 1];
            List<FaceInfo> sideFaces = faces.GetRange(1, faces.Count - 2);
            FaceInfo front = FindClosestFace(sideFaces, forward);

            double frontAngle = Vector3d.VectorAngle(front.HorizontalNormal, forward) * 180.0 / Math.PI;
            if (frontAngle > FrontAngleToleranceDegrees)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"没有侧面与前方向的夹角在 {FrontAngleToleranceDegrees:0}° 以内；最接近的面夹角为 {frontAngle:0.0}°。");
                faceMap = null;
                return false;
            }

            // 观察者站在前方并面向 Box 时，右方为世界 Z 轴叉乘前方向。
            Vector3d observerRightDirection = Vector3d.CrossProduct(Vector3d.ZAxis, forward);
            faceMap = new Dictionary<string, FaceInfo>
            {
                { "前", front },
                { "后", FindClosestFace(sideFaces, -front.HorizontalNormal) },
                { "上", top },
                { "下", bottom },
                { "左", FindClosestFace(sideFaces, -observerRightDirection) },
                { "右", FindClosestFace(sideFaces, observerRightDirection) }
            };
            return true;
        }

        private static List<FaceInfo> GetFaces(Brep boxBrep)
        {
            List<FaceInfo> faces = new List<FaceInfo>();
            for (int i = 0; i < boxBrep.Faces.Count; i++)
            {
                BrepFace face = boxBrep.Faces[i];
                Interval u = face.Domain(0);
                Interval v = face.Domain(1);
                Vector3d normal = face.NormalAt((u.T0 + u.T1) * 0.5, (v.T0 + v.T1) * 0.5);
                normal.Unitize();

                Vector3d horizontalNormal = new Vector3d(normal.X, normal.Y, 0.0);
                horizontalNormal.Unitize();

                faces.Add(new FaceInfo(i, face.GetBoundingBox(true).Center, horizontalNormal));
            }

            return faces;
        }

        private static FaceInfo FindClosestFace(IEnumerable<FaceInfo> faces, Vector3d direction)
        {
            FaceInfo result = null;
            double maxDot = double.NegativeInfinity;

            foreach (FaceInfo face in faces)
            {
                double dot = face.HorizontalNormal * direction;
                if (dot > maxDot)
                {
                    maxDot = dot;
                    result = face;
                }
            }

            return result;
        }

        private static string NormalizeFaceCode(string code)
        {
            return (code ?? string.Empty).Replace(" ", string.Empty).Replace("　", string.Empty);
        }

        private static bool TryParseFaceCode(string code, out string firstFace, out string secondFace)
        {
            firstFace = null;
            secondFace = null;

            if (code.Length != 2)
                return false;

            string first = code.Substring(0, 1);
            string second = code.Substring(1, 1);
            if (first == second || !IsFaceName(first) || !IsFaceName(second) || !IsAdjacentFacePair(first, second))
                return false;

            firstFace = first;
            secondFace = second;

            return true;
        }

        private static bool IsFaceName(string faceName)
        {
            return faceName == "前" || faceName == "后" || faceName == "上" ||
                   faceName == "下" || faceName == "左" || faceName == "右";
        }

        private static bool IsAdjacentFacePair(string first, string second)
        {
            bool firstIsFrontOrBack = first == "前" || first == "后";
            bool secondIsFrontOrBack = second == "前" || second == "后";
            if (firstIsFrontOrBack || secondIsFrontOrBack)
                return !(firstIsFrontOrBack && secondIsFrontOrBack);

            bool firstIsLeftOrRight = first == "左" || first == "右";
            bool secondIsLeftOrRight = second == "左" || second == "右";
            bool firstIsTopOrBottom = first == "上" || first == "下";
            bool secondIsTopOrBottom = second == "上" || second == "下";
            return (firstIsLeftOrRight && secondIsTopOrBottom) ||
                   (secondIsLeftOrRight && firstIsTopOrBottom);
        }

        private static Curve FindSharedEdge(Brep boxBrep, int firstFaceIndex, int secondFaceIndex)
        {
            foreach (BrepEdge edge in boxBrep.Edges)
            {
                int[] adjacentFaces = edge.AdjacentFaces();
                if (adjacentFaces.Length == 2 &&
                    ((adjacentFaces[0] == firstFaceIndex && adjacentFaces[1] == secondFaceIndex) ||
                     (adjacentFaces[0] == secondFaceIndex && adjacentFaces[1] == firstFaceIndex)))
                {
                    return edge.DuplicateCurve();
                }
            }

            return null;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("373C0B31-8603-4C75-870E-F37F850D5005");

        private sealed class FaceInfo
        {
            public FaceInfo(int index, Point3d center, Vector3d horizontalNormal)
            {
                Index = index;
                Center = center;
                HorizontalNormal = horizontalNormal;
            }

            public int Index { get; }
            public Point3d Center { get; }
            public Vector3d HorizontalNormal { get; }
        }
    }
}
