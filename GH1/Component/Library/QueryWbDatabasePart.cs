using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NS_Parrot
{
    /// <summary>按完整编号读取 WB 分类库中的一个零件及其特征信息。</summary>
    public sealed class QueryWbDatabasePart : GH_Component
    {
        public QueryWbDatabasePart()
            : base("查询分类库实体（WB）", "查询实体_WB",
                "按完整编号查询 WB 分类库，并输出零件信息和完整特征数据。",
                "Parrot", "Library")
        {
        }

        public override Guid ComponentGuid => new Guid("A527CB46-4BCE-4FF2-9FAB-6E56E154B8F1");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("数据库地址", "数据库", "WB 分类库 SQLite 文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("要查的编号", "编号", "要查询的完整零件编号，不区分大小写。", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddBooleanParameter("已找到", "找到", "指定编号是否存在于数据库中。", GH_ParamAccess.item);
            p.AddTextParameter("零件信息", "信息", "零件的材质、颜色、尺寸、面积、体积、惯量、孔和外轮廓信息。", GH_ParamAccess.list);
            p.AddTextParameter("完整特征数据", "特征", "可用于后续核查或保存的完整特征文本。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            string number = string.Empty;
            if (!da.GetData(0, ref database) || !da.GetData(1, ref number))
                return;

            bool found = false;
            var information = new List<string>();
            string featureText = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(database))
                    throw new ArgumentException("数据库地址不能为空。", nameof(database));
                if (!File.Exists(database))
                    throw new FileNotFoundException("数据库文件不存在：" + database);
                if (string.IsNullOrWhiteSpace(number))
                    throw new ArgumentException("要查的编号不能为空。", nameof(number));
                if (!WbMigrationStore.TryValidateWbDatabase(database, out string databaseMessage))
                    throw new ArgumentException(databaseMessage);

                string queryNumber = number.Trim();
                WbMigrationRecord record = WbMigrationStore.LoadAllParts(database)
                    .FirstOrDefault(item => string.Equals(item.Number, queryNumber,
                        StringComparison.OrdinalIgnoreCase));
                if (record == null)
                {
                    information.Add("数据库中未找到编号：" + queryNumber);
                }
                else
                {
                    found = true;
                    information.AddRange(FormatInformation(record));
                    featureText = record.Feature.Serialize();
                }
            }
            catch (Exception ex)
            {
                information.Add("查询失败：" + ex.Message);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }

            da.SetData(0, found);
            da.SetDataList(1, information);
            da.SetData(2, featureText);
        }

        private static IEnumerable<string> FormatInformation(WbMigrationRecord record)
        {
            WbFeature feature = record.Feature;
            yield return "编号：" + record.Number;
            yield return "材质：" + DisplayOptionalText(feature.Material);
            yield return "颜色：" + DisplayOptionalText(feature.Color);
            yield return "包围盒：(" + WbUtil.FD(feature.BoundingBox[0]) + ", " +
                WbUtil.FD(feature.BoundingBox[1]) + ", " + WbUtil.FD(feature.BoundingBox[2]) + ")";
            yield return "表面积：" + WbUtil.FD(feature.Area);
            yield return "体积：" + WbUtil.FD(feature.Volume);
            yield return "主惯量：(" + WbUtil.FD(feature.Inertia[0]) + ", " +
                WbUtil.FD(feature.Inertia[1]) + ", " + WbUtil.FD(feature.Inertia[2]) + ")";
            yield return "孔数量：" + feature.Holes.Count;
            for (int index = 0; index < feature.Holes.Count; index++)
            {
                WbHole hole = feature.Holes[index];
                yield return "孔[" + index + "]：局部坐标=(" + WbUtil.FD(hole.U) + ", " +
                    WbUtil.FD(hole.V) + ", " + WbUtil.FD(hole.W) + ")；周长=" +
                    WbUtil.FD(hole.Perimeter) + "；面积=" + WbUtil.FD(hole.Area) +
                    "；法向=(" + WbUtil.FD(hole.NU) + ", " + WbUtil.FD(hole.NV) +
                    ", " + WbUtil.FD(hole.NW) + ")" +
                    (hole.IsDegenerate ? "；退化=" + DisplayOptionalText(hole.DegenerateType) : string.Empty);
            }
            yield return "外轮廓数量：" + feature.OuterProfiles.Count;
            for (int index = 0; index < feature.OuterProfiles.Count; index++)
            {
                WbOuterProfile profile = feature.OuterProfiles[index];
                yield return "外轮廓[" + index + "]：面积=" + WbUtil.FD(profile.Area) +
                    "；周长=" + WbUtil.FD(profile.Perimeter) + "；法向=(" +
                    WbUtil.FD(profile.NU) + ", " + WbUtil.FD(profile.NV) + ", " +
                    WbUtil.FD(profile.NW) + ")";
            }
        }

        private static string DisplayOptionalText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "（空）" : value;
        }
    }
}
