using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

namespace NS_Parrot
{
    public class ExtrudeToSurface : GH_Component
    {
        public ExtrudeToSurface()
          : base("ExtrudeToSurface", "拉伸至面",
              "将闭合平面截面沿方向拉伸，并用目标曲面切割，得到从截面到曲面的实体",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("截面", "截面", "闭合平面曲线，或平的曲面/Brep", GH_ParamAccess.item);
            pManager.AddBrepParameter("目标曲面", "目标曲面", "用于停止拉伸的曲面或Brep，允许非平面", GH_ParamAccess.item);
            pManager.AddVectorParameter("方向", "方向", "拉伸方向；为空时使用截面法向并自动朝向目标曲面", GH_ParamAccess.item, Vector3d.Unset);
            pManager.AddNumberParameter("长度", "长度", "预拉伸长度；小于等于0时自动估算", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("容差", "容差", "几何计算容差；小于等于0时使用Rhino文档绝对容差", GH_ParamAccess.item, 0.0);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("实体", "实体", "拉伸至目标曲面后的实体", GH_ParamAccess.item);
            pManager.AddCurveParameter("截面轮廓", "截面轮廓", "用于拉伸的闭合平面轮廓", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "状态", "运行状态信息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            object sectionInput = null;
            Brep target = null;
            Vector3d direction = Vector3d.Unset;
            double length = 0.0;
            double tolerance = 0.0;

            if (!DA.GetData(0, ref sectionInput)) return;
            if (!DA.GetData(1, ref target)) return;
            DA.GetData(2, ref direction);
            DA.GetData(3, ref length);
            DA.GetData(4, ref tolerance);

            tolerance = ResolveTolerance(tolerance);

            try
            {
                SectionData section = ParseSection(sectionInput, tolerance);
                Vector3d extrusionDirection = ResolveDirection(direction, section.Plane, section.Profile, target);
                double extrusionLength = ResolveLength(length, section.Profile, target, extrusionDirection, tolerance);

                Surface extrusionSurface = Surface.CreateExtrusion(section.Profile, extrusionDirection * extrusionLength);
                Brep rawExtrusion = extrusionSurface?.ToBrep();
                rawExtrusion = rawExtrusion?.CapPlanarHoles(tolerance) ?? rawExtrusion;
                if (rawExtrusion == null)
                    throw new InvalidOperationException("无法从截面生成预拉伸实体。");

                Brep[] pieces = rawExtrusion.Split(target, tolerance);
                if (pieces == null || pieces.Length < 2)
                    throw new InvalidOperationException("目标曲面没有成功切割预拉伸实体，请检查方向、长度或目标曲面范围。");

                Brep result = PickSectionSidePiece(pieces, section.Profile, extrusionDirection, tolerance);
                if (result == null)
                    throw new InvalidOperationException("切割成功，但无法判断应保留哪一段实体。");

                result = result.CapPlanarHoles(tolerance) ?? result;

                DA.SetData(0, result);
                DA.SetData(1, section.Profile);
                DA.SetData(2, string.Format(CultureInfo.InvariantCulture, "完成。预拉伸长度={0:0.###}，分割块数={1}。", extrusionLength, pieces.Length));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(2, ex.Message);
            }
        }

        private static double ResolveTolerance(double tolerance)
        {
            if (tolerance > 0.0)
                return tolerance;

            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            return doc == null ? 0.001 : doc.ModelAbsoluteTolerance;
        }

        private static SectionData ParseSection(object input, double tolerance)
        {
            if (input is GH_ObjectWrapper wrapper)
                return ParseSection(wrapper.Value, tolerance);

            if (input is IGH_Goo goo)
            {
                if (goo.CastTo(out Curve curve))
                    return ParseCurveSection(curve, tolerance);

                if (goo.CastTo(out Brep brep))
                    return ParseBrepSection(brep, tolerance);

                object scriptValue = goo.ScriptVariable();
                if (!ReferenceEquals(scriptValue, input))
                    return ParseSection(scriptValue, tolerance);
            }

            if (input is Curve directCurve)
                return ParseCurveSection(directCurve, tolerance);

            if (input is Surface surface)
                return ParseBrepSection(surface.ToBrep(), tolerance);

            if (input is Brep directBrep)
                return ParseBrepSection(directBrep, tolerance);

            throw new ArgumentException("截面必须是闭合平面曲线，或平的曲面/Brep。");
        }

        private static SectionData ParseCurveSection(Curve curve, double tolerance)
        {
            if (curve == null)
                throw new ArgumentException("截面曲线为空。");

            Curve profile = curve.DuplicateCurve();
            if (profile == null || !profile.IsClosed)
                throw new ArgumentException("截面曲线必须闭合。");

            if (!profile.TryGetPlane(out Plane plane, tolerance))
                throw new ArgumentException("截面曲线必须是平面曲线。");

            Brep[] caps = Brep.CreatePlanarBreps(profile, tolerance);
            if (caps == null || caps.Length == 0)
                throw new ArgumentException("截面曲线无法生成平面底面，请检查曲线是否自交。");

            return new SectionData(profile, plane);
        }

        private static SectionData ParseBrepSection(Brep brep, double tolerance)
        {
            if (brep == null)
                throw new ArgumentException("截面曲面为空。");

            List<Curve> nakedEdges = brep.DuplicateNakedEdgeCurves(true, true)?.ToList() ?? new List<Curve>();
            Curve[] joined = Curve.JoinCurves(nakedEdges, tolerance);
            Curve profile = joined
                .Where(x => x != null && x.IsClosed && x.TryGetPlane(out _, tolerance))
                .OrderByDescending(x => Math.Abs(AreaMassProperties.Compute(x)?.Area ?? 0.0))
                .FirstOrDefault();

            if (profile == null && brep.Faces.Count > 0)
            {
                Brep faceBrep = brep.Faces[0].ToBrep();
                nakedEdges = faceBrep.DuplicateNakedEdgeCurves(true, true)?.ToList() ?? new List<Curve>();
                joined = Curve.JoinCurves(nakedEdges, tolerance);
                profile = joined
                    .Where(x => x != null && x.IsClosed && x.TryGetPlane(out _, tolerance))
                    .OrderByDescending(x => Math.Abs(AreaMassProperties.Compute(x)?.Area ?? 0.0))
                    .FirstOrDefault();
            }

            if (profile == null)
                throw new ArgumentException("平面截面无法提取闭合外轮廓。");

            if (!profile.TryGetPlane(out Plane plane, tolerance))
                throw new ArgumentException("截面曲面/Brep 的外轮廓不是平面轮廓。");

            return new SectionData(profile.DuplicateCurve(), plane);
        }

        private static Vector3d ResolveDirection(Vector3d input, Plane sectionPlane, Curve profile, Brep target)
        {
            Vector3d direction = input.IsValid && input.Length > Rhino.RhinoMath.ZeroTolerance
                ? input
                : sectionPlane.Normal;

            direction.Unitize();

            if (!(input.IsValid && input.Length > Rhino.RhinoMath.ZeroTolerance))
            {
                Point3d sectionCenter = profile.GetBoundingBox(true).Center;
                Point3d targetCenter = target.GetBoundingBox(true).Center;
                if (Vector3d.Multiply(targetCenter - sectionCenter, direction) < 0.0)
                    direction.Reverse();
            }

            return direction;
        }

        private static double ResolveLength(double inputLength, Curve profile, Brep target, Vector3d direction, double tolerance)
        {
            if (inputLength > tolerance)
                return inputLength;

            BoundingBox sectionBox = profile.GetBoundingBox(true);
            BoundingBox targetBox = target.GetBoundingBox(true);
            double sectionMax = MaxProjection(sectionBox, direction);
            double targetMax = MaxProjection(targetBox, direction);
            double distance = targetMax - sectionMax;
            double padding = Math.Max(sectionBox.Diagonal.Length + targetBox.Diagonal.Length, tolerance * 10.0);
            double length = distance + padding;

            if (length <= tolerance)
                throw new InvalidOperationException("目标曲面不在拉伸方向上，请反转方向或输入明确的长度。");

            return length;
        }

        private static Brep PickSectionSidePiece(IEnumerable<Brep> pieces, Curve profile, Vector3d direction, double tolerance)
        {
            BoundingBox sectionBox = profile.GetBoundingBox(true);
            double sectionMin = MinProjection(sectionBox, direction);

            return pieces
                .Where(x => x != null)
                .OrderBy(x => Math.Abs(MinProjection(x.GetBoundingBox(true), direction) - sectionMin))
                .ThenBy(x => MaxProjection(x.GetBoundingBox(true), direction))
                .FirstOrDefault(x => Math.Abs(MinProjection(x.GetBoundingBox(true), direction) - sectionMin) <= tolerance * 20.0)
                ?? pieces.FirstOrDefault();
        }

        private static double MinProjection(BoundingBox box, Vector3d direction)
        {
            return box.GetCorners().Min(point => ProjectPoint(point, direction));
        }

        private static double MaxProjection(BoundingBox box, Vector3d direction)
        {
            return box.GetCorners().Max(point => ProjectPoint(point, direction));
        }

        private static double ProjectPoint(Point3d point, Vector3d direction)
        {
            return point.X * direction.X + point.Y * direction.Y + point.Z * direction.Z;
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("37CBF9E7-FC42-490D-BB40-AA648CA694CD");

        private sealed class SectionData
        {
            public SectionData(Curve profile, Plane plane)
            {
                Profile = profile;
                Plane = plane;
            }

            public Curve Profile { get; }

            public Plane Plane { get; }
        }
    }
}
