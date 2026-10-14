using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace NS_Parrot
{
    /// <summary>直接以 Brep 列表求解空间对应优先的 WB 可行误差设置。</summary>
    public sealed class FindWbClassificationToleranceByBrep : GH_Component
    {
        public FindWbClassificationToleranceByBrep()
            : base("求分类误差（Brep，WB）", "分类误差Brep_WB",
                "为输入 Brep 列表优先保持孔位对应，求解可按“分类库迁移_WB”规则归为同一类的误差设置。",
                "Parrot", "Library") { }

        public override Guid ComponentGuid => new Guid("76D4F21C-3E87-4A9F-8B6B-1D0C5E9A2F48");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddBrepParameter("实体", "Brep", "需要按 WB 分类规则归为同一类的 Brep 列表。", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("误差设置", "设置", "优先孔位对应、带数值余量的 WB 可行误差设置文本。", GH_ParamAccess.item);
            p.AddBooleanParameter("可归为一组", "成功", "按输出设置复核后，全部 Brep 是否两两可按 WB 规则匹配。", GH_ParamAccess.item);
            p.AddTextParameter("设置明细（WB）", "设置明细", "按“分类库迁移_WB”右键菜单的名称与顺序逐行显示。", GH_ParamAccess.list);
            p.AddTextParameter("诊断", "诊断", "无法归为一组时的硬性不匹配原因；成功时说明求解方式。", GH_ParamAccess.list);
            p.AddCurveParameter("差异圈", "圈", "成功时圈出孔位临界孔对；失败时圈出未配齐的孔或实体中心。孔编号见诊断。", GH_ParamAccess.list);
            p.AddBrepParameter("分组", "分组", "按当前 WB 匹配规则分组的 Brep 树；每个分支内的实体两两可匹配。", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            var breps = new List<Brep>();
            if (!da.GetDataList(0, breps) || breps.Count == 0)
                return;

            var diagnostics = new List<string>();
            var markers = new List<Curve>();
            var groups = new GH_Structure<GH_Brep>();
            try
            {
                if (breps.Count < 2)
                    throw new ArgumentException("至少需要输入两个 Brep 才能求分类误差。" );

                double fallbackTolerance = RhinoDoc.ActiveDoc == null
                    ? 0.001
                    : RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
                var settings = new WbClassificationSettings { StrictMaterialColor = true, FallbackTolerance = fallbackTolerance };
                var features = new List<WbFeature>();
                for (int i = 0; i < breps.Count; i++)
                    features.Add(WbFeature.Create(string.Empty, string.Empty, breps[i],
                        settings.InertiaDegenerateRatio, fallbackTolerance, i));

                FindWbClassificationTolerance.SetMostPermissive(settings, breps);
                if (!FindWbClassificationTolerance.AllPairsMatch(features, settings))
                {
                    diagnostics.Add("即使所有数值误差均放宽到极大值，仍无法按 WB 规则归为一组。" );
                    diagnostics.AddRange(FindWbClassificationTolerance.FindHardMismatchDiagnostics(features, settings));
                    markers = FindWbClassificationTolerance.BuildMismatchMarkers(features, breps, settings);
                    diagnostics.Add("已圈出 " + markers.Count.ToString(CultureInfo.InvariantCulture) + " 个可能不一致的位置。");
                    groups = FindWbClassificationTolerance.BuildGroups(features, breps, settings);
                    diagnostics.Add("已按当前 WB 匹配规则拆分为 " + groups.PathCount.ToString(CultureInfo.InvariantCulture) + " 组输出。");
                    da.SetData(0, string.Empty);
                    da.SetData(1, false);
                    da.SetDataList(2, new List<string>());
                    da.SetDataList(3, diagnostics);
                    da.SetDataList(4, markers);
                    da.SetDataTree(5, groups);
                    return;
                }

                FindWbClassificationTolerance.OptimizeSpatialTolerances(features, settings, diagnostics);

                bool success = FindWbClassificationTolerance.AllPairsMatch(features, settings);
                diagnostics.Add(success
                    ? "已优先确定孔位对应，再收紧其它判据，并完成全部 Brep 的两两复核。"
                    : "误差求解后复核失败；请将诊断信息反馈以便排查。" );
                if (success) markers = FindWbClassificationTolerance.BuildCriticalHoleMarkers(features, breps, settings, diagnostics);
                groups = FindWbClassificationTolerance.BuildGroups(features, breps, settings);
                da.SetData(0, settings.SettingsText());
                da.SetData(1, success);
                da.SetDataList(2, FindWbClassificationTolerance.FormatDetails(settings));
                da.SetDataList(3, diagnostics);
                da.SetDataList(4, markers);
                da.SetDataTree(5, groups);
            }
            catch (Exception ex)
            {
                diagnostics.Add("求解失败：" + ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                da.SetData(1, false);
                da.SetDataList(3, diagnostics);
                da.SetDataList(4, markers);
                da.SetDataTree(5, groups);
            }
        }
    }
}
