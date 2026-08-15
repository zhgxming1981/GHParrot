using System;
using System.Collections.Generic;
using CommonFunction;
using Grasshopper.Kernel;

namespace NS_Parrot
{
    public class FindTextIndex : GH_Component
    {
        public FindTextIndex()
            : base("查找文本索引", "查找索引",
                "查找 Find 在 FindIn 文本列表中出现的位置。",
                "Parrot", "Data")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("FindIn", "FindIn", "被查找的文本列表。", GH_ParamAccess.list);
            pManager.AddTextParameter("Find", "Find", "要查找的文本列表。", GH_ParamAccess.list);
            pManager.AddBooleanParameter("是否区分大小写", "大小写", "True 区分大小写；False 不区分大小写。", GH_ParamAccess.item, true);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("全等索引", "全等", "FindIn 中与任一 Find 完全相同的索引。", GH_ParamAccess.list);
            pManager.AddIntegerParameter("包含索引", "包含", "FindIn 中包含任一 Find 的索引。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<string> findIn = new List<string>();
            if (!DA.GetDataList(0, findIn))
                return;

            List<string> find = new List<string>();
            if (!DA.GetDataList(1, find))
                return;

            bool caseSensitive = true;
            DA.GetData(2, ref caseSensitive);

            StringComparison comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            List<int> equalIndices = new List<int>();
            List<int> containsIndices = new List<int>();

            for (int i = 0; i < findIn.Count; i++)
            {
                string source = findIn[i] ?? string.Empty;
                foreach (string target in find)
                {
                    string keyword = target ?? string.Empty;
                    if (string.Equals(source, keyword, comparison))
                    {
                        equalIndices.Add(i);
                        break;
                    }
                }

                foreach (string target in find)
                {
                    string keyword = target ?? string.Empty;
                    if (source.IndexOf(keyword, comparison) >= 0)
                    {
                        containsIndices.Add(i);
                        break;
                    }
                }
            }

            DA.SetDataList(0, equalIndices);
            DA.SetDataList(1, containsIndices);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("6A40913A-69F5-4AFC-A6F5-CA27B49AFC7B"); }
        }
    }
}
