using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace NS_Parrot
{
    /// <summary>
    /// 根据指定方向对 Box 的六个面进行前后上下左右分类。
    /// </summary>
    public class ClassifyBoxFaces : GH_Component
    {
        private const double FrontAngleToleranceDegrees = 30.0;

        public ClassifyBoxFaces()
            : base("Box面分类", "Box面分类",
                "根据方向向量识别 Box 的前、后、上、下、左、右六个面。上下按面中心的 Z 坐标确定；前面与方向向量的夹角须不超过 30°。",
                "Parrot", "Geometry")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBoxParameter("盒子", "Box", "需要分类的 Box。", GH_ParamAccess.item);
            pManager.AddVectorParameter("前方向", "V", "指向前方的向量；仅使用其 XY 方向。", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("前面", "前", "朝向输入向量的面。", GH_ParamAccess.item);
            pManager.AddBrepParameter("后面", "后", "前面的相对面。", GH_ParamAccess.item);
            pManager.AddBrepParameter("上面", "上", "面中心 Z 坐标最高的面。", GH_ParamAccess.item);
            pManager.AddBrepParameter("下面", "下", "面中心 Z 坐标最低的面。", GH_ParamAccess.item);
            pManager.AddBrepParameter("左面", "左", "面对前方时位于左侧的面。", GH_ParamAccess.item);
            pManager.AddBrepParameter("右面", "右", "面对前方时位于右侧的面。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Box box = new Box();
            Vector3d forward = Vector3d.Unset;

            if (!DA.GetData(0, ref box) || !DA.GetData(1, ref forward))
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
                return;
            }

            FaceInfo back = FindClosestFace(sideFaces, -front.HorizontalNormal);
            // 观察者站在前方并面向 Box 时，右方为世界 Z 轴叉乘前方向。
            Vector3d observerRightDirection = Vector3d.CrossProduct(Vector3d.ZAxis, forward);
            FaceInfo right = FindClosestFace(sideFaces, observerRightDirection);
            FaceInfo left = FindClosestFace(sideFaces, -observerRightDirection);

            DA.SetData(0, front.Brep);
            DA.SetData(1, back.Brep);
            DA.SetData(2, top.Brep);
            DA.SetData(3, bottom.Brep);
            DA.SetData(4, left.Brep);
            DA.SetData(5, right.Brep);
        }

        private static List<FaceInfo> GetFaces(Brep boxBrep)
        {
            List<FaceInfo> faces = new List<FaceInfo>();
            foreach (BrepFace face in boxBrep.Faces)
            {
                Interval u = face.Domain(0);
                Interval v = face.Domain(1);
                Vector3d normal = face.NormalAt((u.T0 + u.T1) * 0.5, (v.T0 + v.T1) * 0.5);
                normal.Unitize();

                Vector3d horizontalNormal = new Vector3d(normal.X, normal.Y, 0.0);
                horizontalNormal.Unitize();

                faces.Add(new FaceInfo(face.ToBrep(), face.GetBoundingBox(true).Center, normal, horizontalNormal));
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

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("9F3E035A-0313-4BB9-86E3-0B6468D8F1B2");

        private sealed class FaceInfo
        {
            public FaceInfo(Brep brep, Point3d center, Vector3d normal, Vector3d horizontalNormal)
            {
                Brep = brep;
                Center = center;
                Normal = normal;
                HorizontalNormal = horizontalNormal;
            }

            public Brep Brep { get; }
            public Point3d Center { get; }
            public Vector3d Normal { get; }
            public Vector3d HorizontalNormal { get; }
        }
    }
}
