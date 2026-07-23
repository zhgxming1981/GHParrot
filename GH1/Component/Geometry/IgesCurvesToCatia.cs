using CatiaFunction;
using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NS_Parrot
{
    public class IgesCurvesToCatia : GH_Component
    {
        public IgesCurvesToCatia()
          : base("IGES定位曲线", "IGES定位",
              "将 Rhino 曲线无损导出 IGES，并作为无链接 CATIA 几何图形集导入。圆弧保持圆弧，NURBS 保持 NURBS。",
              "Parrot", "Geometry") { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddCurveParameter("曲线", "C", "按 {a;b} 组织的 Rhino 曲线；同一 a 合并为一个 CATIA 定位几何图形集。", GH_ParamAccess.tree);
            p.AddPlaneParameter("目标平面", "P", "CATIA 中曲线投影到的平面；未输入时使用第一条曲线所在平面。", GH_ParamAccess.item);
            p[1].Optional = true;
            p.AddTextParameter("名称", "N", "CATIA 几何图形集名称。", GH_ParamAccess.item, "定位曲线");
            p.AddBooleanParameter("执行", "R", "为 True 时导出并导入 CATIA。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("状态", "S", "执行结果或错误信息。", GH_ParamAccess.item);
            p.AddTextParameter("IGES文件", "F", "每个 a 对应的 IGES 文件。", GH_ParamAccess.list);
            p.AddIntegerParameter("数量", "N", "每个 a 导入 CATIA 的几何数量。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality()) return;
            GH_Structure<GH_Curve> curveTree;
            Plane targetPlane = Plane.Unset;
            string name = "定位曲线";
            bool execute = false;
            if (!DA.GetDataTree(0, out curveTree) || curveTree.DataCount == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请至少输入一条曲线。"); return; }
            var groups = new SortedDictionary<int, List<Curve>>();
            foreach (var path in curveTree.Paths)
            {
                int groupIndex = path.Length > 0 ? path[0] : 0;
                if (!groups.TryGetValue(groupIndex, out List<Curve> group)) groups[groupIndex] = group = new List<Curve>();
                foreach (GH_Curve goo in curveTree.get_Branch(path)) if (goo?.Value != null) group.Add(goo.Value);
            }
            if (groups.Count == 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "输入中没有有效曲线。"); return; }
            if (!DA.GetData(1, ref targetPlane) && !groups.First().Value[0].TryGetPlane(out targetPlane)) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "请提供目标平面，或输入可取得平面的曲线。"); return; }
            DA.GetData(2, ref name);
            DA.GetData(3, ref execute);
            if (!execute) { DA.SetData(0, "未执行：将“执行”设为 True 后导入 CATIA。"); return; }

            try
            {
                var catia = new Common4Catia();
                var allCurves = groups.SelectMany(pair => pair.Value).ToList();
                string filePath = Path.Combine(Path.GetTempPath(), "Parrot", $"{SanitizeFileName(name)}_{Guid.NewGuid():N}.igs");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                ExportIges(allCurves, filePath);
                int count = catia.ImportIgesAsDatumCurves(filePath, name, targetPlane);
                DA.SetData(0, $"已通过单一临时 IGES 文档复制 {count} 条曲线到当前 CATPart。分组数：{groups.Count}。");
                DA.SetDataList(1, new[] { filePath });
                DA.SetDataList(2, new[] { count });
            }
            catch (Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); DA.SetData(0, "导入失败：" + ex.Message); }
        }

        private static void ExportIges(IEnumerable<Curve> curves, string filePath)
        {
            using (RhinoDoc document = RhinoDoc.CreateHeadless(null))
            {
                foreach (Curve curve in curves)
                {
                    if (curve == null || !curve.IsValid) throw new InvalidOperationException("输入中包含无效曲线。");
                    Guid id = document.Objects.AddCurve(curve.DuplicateCurve());
                    document.Objects.Select(id);
                }

                var options = new FileIgsWriteOptions
                {
                    Units = RhinoDoc.ActiveDoc?.ModelUnitSystem ?? Rhino.UnitSystem.Millimeters,
                    Scale = 1.0,
                    CurveMaxDegree = FileIgsWriteOptions.MaxDegreeMode.MdNoLimit,
                    SimplifyCurves = true,
                    FitRationalCurves = false,
                    CompositeCurvesAsSingleBsplines = false
                };
                if (!document.ExportSelected(filePath, options.ToDictionary())) throw new InvalidOperationException("Rhino IGES 导出失败。");
            }
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            return string.IsNullOrWhiteSpace(value) ? "定位曲线" : value;
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_Sketch");
        public override Guid ComponentGuid => new Guid("5A01E1D6-7EC2-4922-A748-555285D0F87C");
    }
}
