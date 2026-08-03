using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CompareEntity : GH_Component
    {
        internal bool ButtonRun { get; set; }
        internal enum ButtonColor { Black, Grey }
        internal ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;

        private List<Guid> _lastResult = new List<Guid>();
        private string _status = string.Empty;

        public CompareEntity()
            : base("比较实体", "比较实体",
                  "以第一个文件中的实体为基准，将其它文件中的指定实体对齐后Bake到Rhino",
                  "Parrot", "Rhino")
        {
        }

        public override void CreateAttributes()
        {
            m_attributes = new CompareEntity_Attributes(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径列表", "文件",
                "待比较的 .3dm 文件路径", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "图层",
                "要搜索的图层名列表，同一文件在所有指定图层中搜索到的实体总数应为1",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("已Bake实体的GUID", "GUID",
                "Bake 后各实体在Rhino文档中的 GUID", GH_ParamAccess.list);
            pManager.AddTextParameter("状态", "状态",
                "操作日志或错误信息", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> filePaths = new List<string>();
            List<string> layerNames = new List<string>();

            if (!DA.GetDataList(0, filePaths)) { return; }
            DA.GetDataList(1, layerNames);

            if (!ButtonRun)
            {
                DA.SetDataList(0, _lastResult.Select(g => new GH_Guid(g)));
                DA.SetData(1, _status);
                return;
            }

            ButtonRun = false;
            _lastResult.Clear();
            _status = string.Empty;

            var logs = new List<string>();

            try
            {
                if (filePaths.Count == 0)
                {
                    _status = "输入列表为空";
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                if (layerNames.Count == 0)
                {
                    _status = "图层列表为空";
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                // 读取每个文件指定图层的实体
                List<GeometryBase> geometries = new List<GeometryBase>();
                List<string> names = new List<string>();

                for (int i = 0; i < filePaths.Count; i++)
                {
                    string filePath = filePaths[i];

                    if (string.IsNullOrWhiteSpace(filePath))
                    {
                        logs.Add("第" + i + "个文件路径为空，跳过");
                        continue;
                    }

                    if (!File.Exists(filePath))
                    {
                        logs.Add("文件不存在，跳过：" + filePath);
                        continue;
                    }

                    File3dm file = File3dm.Read(filePath);
                    if (file == null)
                    {
                        logs.Add("无法读取文件，跳过：" + filePath);
                        continue;
                    }

                    // 在所有指定图层中搜索对象（参考按图层导入的 FindSourceLayerIndices）
                    HashSet<int> matchedLayerIndices = FindSourceLayerIndices(
                        file, layerNames, logs, filePath);
                    if (matchedLayerIndices.Count == 0)
                        continue;

                    List<File3dmObject> layerObjects = new List<File3dmObject>();
                    foreach (File3dmObject obj in file.Objects)
                    {
                        if (obj?.Attributes == null || obj.Geometry == null)
                            continue;
                        if (matchedLayerIndices.Contains(obj.Attributes.LayerIndex))
                            layerObjects.Add(obj);
                    }

                    if (layerObjects.Count != 1)
                    {
                        logs.Add("文件 " + Path.GetFileName(filePath)
                            + " 图层列表中共有 " + layerObjects.Count
                            + " 个实体（需要恰好1个），跳过");
                        continue;
                    }

                    GeometryBase geo = layerObjects[0].Geometry.Duplicate();
                    if (geo == null)
                    {
                        logs.Add("无法复制文件 " + Path.GetFileName(filePath)
                            + " 指定图层的几何体，跳过");
                        continue;
                    }

                    geometries.Add(geo);
                    names.Add(GetRelativePath(filePath));
                }

                if (geometries.Count == 0)
                {
                    _status = "未获取到任何有效实体。" + string.Join("；", logs);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _status);
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                // 以第一个实体为基准对齐
                BoundingBox baseBBox = geometries[0].GetBoundingBox(true);
                if (!baseBBox.IsValid)
                {
                    _status = "基准实体的包围盒无效";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                Point3d baseCenter = baseBBox.Center;

                for (int i = 1; i < geometries.Count; i++)
                {
                    BoundingBox currentBBox = geometries[i].GetBoundingBox(true);
                    if (!currentBBox.IsValid)
                    {
                        logs.Add("第" + i + "个实体包围盒无效，跳过对齐");
                        continue;
                    }

                    Vector3d offset = baseCenter - currentBBox.Center;
                    geometries[i].Translate(offset);
                }

                // Bake 到当前 Rhino 文档
                RhinoDoc doc = RhinoDoc.ActiveDoc;
                if (doc == null)
                {
                    _status = "没有打开的 Rhino 文档";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                    DA.SetDataList(0, new List<GH_Guid>());
                    DA.SetData(1, _status);
                    return;
                }

                for (int i = 0; i < geometries.Count; i++)
                {
                    ObjectAttributes attrs = doc.CreateDefaultAttributes();
                    attrs.Name = names[i];

                    Guid bakedId = doc.Objects.Add(geometries[i], attrs);
                    if (bakedId == Guid.Empty)
                    {
                        logs.Add("第" + i + "个实体 Bake 失败（" + names[i] + "）");
                    }
                    else
                    {
                        _lastResult.Add(bakedId);
                    }
                }

                doc.Views.Redraw();

                _status = "成功 Bake " + _lastResult.Count + " 个实体";
                if (logs.Count > 0)
                    _status += "。" + string.Join("；", logs);
                DA.SetDataList(0, _lastResult.Select(g => new GH_Guid(g)));
                DA.SetData(1, _status);
            }
            catch (Exception ex)
            {
                _status = "异常：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                DA.SetDataList(0, new List<GH_Guid>());
                DA.SetData(1, _status);
            }
        }

        /// <summary>
        /// 取路径的末尾最多3级作为相对路径。
        /// c:\全部实体\7F\M01\A.3dm → 7F\M01\A.3dm
        /// </summary>
        private static string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
                return string.Empty;

            char[] separators = { '\\', '/' };
            string[] parts = fullPath.Split(separators, StringSplitOptions.RemoveEmptyEntries);

            int take = Math.Min(3, parts.Length);
            return string.Join("\\", parts.Skip(parts.Length - take));
        }

        /// <summary>
        /// 参考 ImportRhinoObjectsByLayer.FindSourceLayerIndices：
        /// 先匹配 FullPath，再匹配短 Name，* 后缀做前缀匹配。
        /// </summary>
        private static HashSet<int> FindSourceLayerIndices(
            File3dm file, List<string> layerNames, List<string> logs, string filePath)
        {
            var result = new HashSet<int>();

            foreach (string rawName in layerNames)
            {
                string layerName = rawName?.Trim();
                if (string.IsNullOrWhiteSpace(layerName))
                    continue;

                // * 后缀 → FullPath 前缀匹配
                if (layerName.EndsWith("*", StringComparison.Ordinal))
                {
                    string prefix = layerName.Substring(0, layerName.Length - 1);
                    bool found = false;
                    foreach (Layer layer in file.AllLayers)
                    {
                        string fullPath = layer.FullPath ?? layer.Name ?? "";
                        if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(layer.Index);
                            found = true;
                        }
                    }
                    if (!found)
                        logs.Add("文件 " + Path.GetFileName(filePath)
                            + " 中找不到匹配图层：「" + layerName + "」");
                    continue;
                }

                // FullPath 精确匹配
                List<Layer> fullPathMatches = new List<Layer>();
                foreach (Layer layer in file.AllLayers)
                {
                    if (string.Equals(layer.FullPath, layerName, StringComparison.OrdinalIgnoreCase))
                        fullPathMatches.Add(layer);
                }
                if (fullPathMatches.Count > 0)
                {
                    foreach (Layer layer in fullPathMatches)
                        result.Add(layer.Index);
                    continue;
                }

                // 短 Name 精确匹配
                List<Layer> nameMatches = new List<Layer>();
                foreach (Layer layer in file.AllLayers)
                {
                    if (string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase))
                        nameMatches.Add(layer);
                }
                if (nameMatches.Count == 0)
                {
                    logs.Add("文件 " + Path.GetFileName(filePath)
                        + " 中找不到图层：「" + layerName + "」");
                    continue;
                }
                foreach (Layer layer in nameMatches)
                    result.Add(layer.Index);
            }

            return result;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.GetCompareEntity(); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("7A2E5F1C-9D4B-4A81-B3E6-8F0C12D5A678"); }
        }
    }

    /// <summary>
    /// Bake 按钮渲染和交互
    /// </summary>
    internal sealed class CompareEntity_Attributes : GH_ComponentAttributes
    {
        public CompareEntity_Attributes(CompareEntity owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            CompareEntity owner = (CompareEntity)Owner;
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20f);
            buttonRect.Inflate(-5f, -2f);

            GH_Palette palette = owner.CurrentButtonColor == CompareEntity.ButtonColor.Black
                ? GH_Palette.Black
                : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.DrawString("Bake", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(
            GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(
                Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20f);

            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                CompareEntity owner = (CompareEntity)Owner;
                owner.CurrentButtonColor = CompareEntity.ButtonColor.Grey;
                owner.ButtonRun = true;
                owner.ExpireSolution(true);
                CMath.Delay(50);
                owner.CurrentButtonColor = CompareEntity.ButtonColor.Black;
                owner.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return GH_ObjectResponse.Ignore;
        }
    }
}
