using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using System;

namespace NS_Parrot
{
    public class DataGate : GH_Component
    {
        private GH_Structure<IGH_Goo> _cachedData = new GH_Structure<IGH_Goo>();
        private bool _lastRefresh;
        private bool _hasCachedData;

        public DataGate()
          : base("数据闸门", "数据闸门",
              "按刷新按钮转发并缓存上游数据，用于阻止参数修改立即触发下游重计算",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "数据", "需要转发给下游的数据，支持 item、list、tree。例：接入阵列电池输出的螺栓线。", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("刷新", "刷新", "接 Button 或 Boolean。只有从 False 变为 True 的瞬间，才会把当前数据转发并缓存。", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("保持", "保持", "True 时，未刷新也继续输出上一次缓存的数据；False 时，未刷新输出空数据。建议保持 True。", GH_ParamAccess.item, true);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "数据", "转发后的数据。刷新时输出当前输入数据；保持=True 时输出上一次缓存数据。", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Done", "Done", "本次刷新成功转发数据时为 True；其它时候为 False。可用于驱动下游执行端口。", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "状态", "当前状态说明，例如：已转发、保持旧数据、等待刷新、无数据。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<IGH_Goo> inputData;
            DA.GetDataTree(0, out inputData);

            bool refresh = false;
            bool keep = true;
            DA.GetData(1, ref refresh);
            DA.GetData(2, ref keep);

            bool risingEdge = refresh && !_lastRefresh;
            bool done = false;
            string status;

            if (risingEdge)
            {
                _cachedData = DuplicateTree(inputData);
                _hasCachedData = _cachedData.DataCount > 0;
                done = _hasCachedData;
                status = _hasCachedData ? "已转发当前数据。" : "已刷新，但输入数据为空。";
            }
            else if (keep && _hasCachedData)
            {
                status = "保持上一次转发的数据。";
            }
            else
            {
                status = keep ? "等待刷新。" : "未刷新，输出空数据。";
            }

            if ((risingEdge && _hasCachedData) || (keep && _hasCachedData))
                DA.SetDataTree(0, DuplicateTree(_cachedData));
            else
                DA.SetDataTree(0, new GH_Structure<IGH_Goo>());

            DA.SetData(1, done);
            DA.SetData(2, status);

            _lastRefresh = refresh;
        }

        private static GH_Structure<IGH_Goo> DuplicateTree(GH_Structure<IGH_Goo> source)
        {
            GH_Structure<IGH_Goo> result = new GH_Structure<IGH_Goo>();
            if (source == null)
                return result;

            foreach (GH_Path path in source.Paths)
            {
                foreach (IGH_Goo item in source.get_Branch(path))
                    result.Append(item?.Duplicate(), path);
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_PulseTrigger");

        public override Guid ComponentGuid => new Guid("D05B54C9-8384-4572-BFB7-9C64D9106E9F");
    }
}
