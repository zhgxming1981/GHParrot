using AutoCADFunction;
using CommonFunction;
using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using parrot.Properties;
using Rhino.FileIO;
using Rhino.Geometry;
using Rhino.Runtime;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CAD2GH : GH_Component
    {
        private const string PersistenceChunk = "CadImportCache";
        private const int PersistenceVersion = 1;

        /// <summary>
        /// Initializes a new instance of the CAD2GH class.
        /// </summary>
        public CAD2GH()
          : base("CAD2GH", "CAD2GH",
              "将CAD中的直线、圆弧、多段线导入到Rhino中",
              "Parrot", "ExcelCAD")
        {
        }


        public enum ButtonColor { Black, Grey }//按钮颜色
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;//当前的按钮颜色

        public List<object> theBakeGeoList = new List<object>();//将要bake的对象
  
        private List<RhinoResult> theRhinoResultList = new List<RhinoResult>();
        public string layerName = "";


        // 🔥 全局去重集合（核心优化）
        HashSet<string> theHandleSet = new HashSet<string>();

        // 错误信息
        string theErrorMessage = "";
        private int _pendingUiRefresh = 0;

        private enum PersistedGeometryKind
        {
            None = 0,
            GeometryBase = 1,
            TextEntity = 2,
            Point3d = 3
        }



        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("pt", "pt", "CAD中的基点", GH_ParamAccess.item);

            Point3d origin = new Point3d(0, 0, 0);
            Vector3d X_axis = new Vector3d(1, 0, 0);
            Vector3d Y_axis = new Vector3d(0, 1, 0);
            Plane plane = new Plane(origin, X_axis, Y_axis);
            pManager.AddPlaneParameter("PL", "PL", "Rhion中的局部坐标平面", GH_ParamAccess.item, plane);
            pManager.AddTextParameter("Layer", "La", "Bake的目标图层", GH_ParamAccess.item, "AutoCAD");

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGeometryParameter("实体", "实体", "实体", GH_ParamAccess.list);
            pManager.AddTextParameter("图层", "图层", "图层", GH_ParamAccess.list);
            pManager.AddColourParameter("颜色", "颜色", "颜色", GH_ParamAccess.list);
            pManager.AddTextParameter("线型", "线型", "线型", GH_ParamAccess.list);
            pManager.AddTextParameter("句柄", "句柄", "线型", GH_ParamAccess.list);
            pManager.AddTextParameter("块名", "块名", "块名", GH_ParamAccess.list);
            pManager.AddTextParameter("文件名", "文件名", "导入对象所在的CAD文件名", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            theBakeGeoList.Clear();

            Point3d insert = Point3d.Origin;
            DA.GetData(0, ref insert);

            Plane plane1 = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);
            Plane plane2 = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);
            DA.GetData(1, ref plane2);

            DA.GetData(2, ref layerName);

            // 🔥 统一变换
            Transform xform = Transform.PlaneToPlane(plane1, plane2);

            List<object> outputList = new List<object>();
            List<string> layerList = new List<string>();
            List<System.Drawing.Color> colorList = new List<System.Drawing.Color>();
            List<string> lineTypeList = new List<string>();
            List<string> handleList = new List<string>();
            List<string> blockNameList = new List<string>();

            // 🔥 错误收集
            List<string> errorList = new List<string>();

            // ============================
            // 核心循环（保持你的结构风格）
            // ============================
            foreach (var r in theRhinoResultList)
            {
                object obj = r.Geometry;
                object outGeo = null;
                bool hasCadError = !string.IsNullOrWhiteSpace(r.ErrorMessage);

                try
                {
                    if (obj is GeometryBase geo)
                    {
                        GeometryBase g = geo.Duplicate();
                        g.Transform(xform);
                        outGeo = g;
                    }
                    else if (obj is TextEntity txt)
                    {
                        TextEntity t = txt.Duplicate() as TextEntity;
                        t.Transform(xform);
                        outGeo = t;
                    }
                    else if (obj is Point3d pt)
                    {
                        Point3d p = pt;
                        p.Transform(xform);
                        outGeo = p;
                    }
                }
                catch (Exception ex)
                {
                    errorList.Add($"Handle={r.Handle} 变换失败: {ex.Message}");
                }

                // ✅ 来自 CAD 转换阶段的错误
                if (hasCadError)
                {
                    string errorMessage = r.ErrorMessage.Trim();
                    errorMessage = errorMessage.TrimStart('|').Trim();

                    if (errorMessage.StartsWith("Handle=", StringComparison.OrdinalIgnoreCase))
                        errorList.Add(errorMessage);
                    else
                        errorList.Add($"Handle={r.Handle} : {errorMessage}");
                }

                if (hasCadError || outGeo == null)
                    continue;

                outputList.Add(outGeo);
                layerList.Add(r.Layer);
                colorList.Add(r.Color);
                lineTypeList.Add(r.LineType);
                handleList.Add(r.Handle);
                blockNameList.Add(r.BlockName);
            }

            theBakeGeoList.AddRange(outputList);

            // ============================
            // 🔥 GH 气泡
            // ============================
            if (errorList.Count > 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, string.Join("\n", errorList));
            }

            // ============================
            // 输出（保持你原结构）
            // ============================
            DA.SetDataList(0, outputList);
            DA.SetDataList(1, layerList);
            DA.SetDataList(2, colorList);
            DA.SetDataList(3, lineTypeList);
            DA.SetDataList(4, handleList);
            DA.SetDataList(5, blockNameList);
            DA.SetData(6, GetFileName(theRhinoResultList));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                //return Resources.IconForThisComponent;
                return Resources.Cad2Rhino;
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_Refresh(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item0 = new ToolStripMenuItem();
            item0.Text = "连接CAD";
            item0.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item0);
            item0.Click += ConnectAutoCAD;

            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "获取CAD元素";
            item1.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item1);
            item1.Click += GetEntityFromAutoCAD;

            ToolStripMenuItem item2 = new ToolStripMenuItem();
            item2.Text = "加选";
            item2.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item2);
            item2.Click += AddEntity;

            ToolStripMenuItem item3 = new ToolStripMenuItem();
            item3.Text = "减选";
            item3.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item3);
            item3.Click += RemoveEntity;

            ToolStripMenuItem item4 = new ToolStripMenuItem();
            item4.Text = "清空";
            item4.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item4);
            item4.Click += ClearEntity;
        }

        void ConnectAutoCAD(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            AutoCADTool.ConnectCAD();
        }

    


        void GetEntityFromAutoCAD(object sender, EventArgs e)
        {
            AutoCADTool.CAD2Rhino((res) =>
            {
                SetRhinoResults(res);
                RequestSafeUiRefresh();
            });
        }

     

        void AddEntity(object sender, EventArgs e)
        {
            AutoCADTool.CAD2Rhino((value) =>
            {
                theErrorMessage = "";

                foreach (var v in value)
                {
                    string handle = v.Handle;

                    if (string.IsNullOrEmpty(handle))
                        continue;

                    // ✅ O(1) 查重
                    if (theHandleSet.Add(handle))
                    {
                        theRhinoResultList.Add(v);
                    }

                    // 收集错误
                    if (!string.IsNullOrEmpty(v.ErrorMessage))
                    {
                        theErrorMessage += v.ErrorMessage + "\n";
                    }
                }

                RequestSafeUiRefresh();
            });
        }

      


        void RemoveEntity(object sender, EventArgs e)
        {
            AutoCADTool.CAD2Rhino((value) =>
            {
                // 🔥 要删除的 handle
                HashSet<string> removeSet = new HashSet<string>();

                foreach (var v in value)
                {
                    if (!string.IsNullOrEmpty(v.Handle))
                        removeSet.Add(v.Handle);
                }

                theErrorMessage = "";

                // 🔥 重建（O(n)）
                List<RhinoResult> newList = new List<RhinoResult>();
                HashSet<string> newHandleSet = new HashSet<string>();

                foreach (var r in theRhinoResultList)
                {
                    if (!removeSet.Contains(r.Handle))
                    {
                        newList.Add(r);
                        newHandleSet.Add(r.Handle);
                    }
                }

                // 替换
                theRhinoResultList = newList;
                theHandleSet = newHandleSet;

                // 收集错误
                foreach (var v in value)
                {
                    if (!string.IsNullOrEmpty(v.ErrorMessage))
                    {
                        theErrorMessage += v.ErrorMessage + "\n";
                    }
                }

                RequestSafeUiRefresh();
            });
        }

        private void RequestSafeUiRefresh()
        {
            if (Interlocked.Exchange(ref _pendingUiRefresh, 1) == 1)
                return;

            Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 80;
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    timer.Dispose();

                    try
                    {
                        StabilizeRhinoUi();
                        ExpireSolution(true);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _pendingUiRefresh, 0);
                    }
                };
                timer.Start();
            }));
        }

        private static void StabilizeRhinoUi()
        {
            Rhino.RhinoApp.Wait();
            Application.DoEvents();

            var doc = Rhino.RhinoDoc.ActiveDoc;
            if (doc != null)
                doc.Views.Redraw();

            Rhino.RhinoApp.Wait();
        }


        void ClearEntity(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            theRhinoResultList.Clear();
            theHandleSet.Clear();
            ExpireSolution(true);//告诉系统，电池需要重新计算
        }

        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32("PersistenceVersion", PersistenceVersion);

            GH_IWriter cacheChunk = writer.CreateChunk(PersistenceChunk);
            cacheChunk.SetInt32("Count", theRhinoResultList.Count);

            for (int i = 0; i < theRhinoResultList.Count; i++)
            {
                GH_IWriter itemChunk = cacheChunk.CreateChunk("Item", i);
                WriteRhinoResult(itemChunk, theRhinoResultList[i]);
            }

            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            theRhinoResultList.Clear();
            theHandleSet.Clear();

            if (reader.FindChunk(PersistenceChunk) is GH_IReader cacheChunk)
            {
                int count = 0;
                cacheChunk.TryGetInt32("Count", ref count);

                List<RhinoResult> restored = new List<RhinoResult>();

                for (int i = 0; i < count; i++)
                {
                    GH_IReader itemChunk = cacheChunk.FindChunk("Item", i);
                    if (itemChunk == null)
                        continue;

                    RhinoResult restoredItem = ReadRhinoResult(itemChunk);
                    if (restoredItem != null)
                        restored.Add(restoredItem);
                }

                SetRhinoResults(restored);
            }

            return base.Read(reader);
        }

        private void SetRhinoResults(IEnumerable<RhinoResult> results)
        {
            theRhinoResultList = results?.ToList() ?? new List<RhinoResult>();
            RebuildHandleSet();
        }

        private void RebuildHandleSet()
        {
            theHandleSet = new HashSet<string>(
                theRhinoResultList
                    .Select(r => r?.Handle)
                    .Where(h => !string.IsNullOrWhiteSpace(h)));
        }

        private static string GetFileName(IEnumerable<RhinoResult> results)
        {
            return results?
                .Select(r => r?.FileName)
                .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f)) ?? string.Empty;
        }

        private static void WriteRhinoResult(GH_IWriter writer, RhinoResult result)
        {
            writer.SetString("Layer", result?.Layer ?? string.Empty);
            writer.SetDrawingColor("Color", result?.Color ?? Color.White);
            writer.SetString("LineType", result?.LineType ?? string.Empty);
            writer.SetString("Handle", result?.Handle ?? string.Empty);
            writer.SetString("BlockName", result?.BlockName ?? string.Empty);
            writer.SetString("ErrorMessage", result?.ErrorMessage ?? string.Empty);
            writer.SetString("FileName", result?.FileName ?? string.Empty);

            PersistedGeometryKind kind = GetPersistedGeometryKind(result?.Geometry);
            writer.SetInt32("GeometryKind", (int)kind);
            writer.SetString("GeometryRuntimeType", result?.Geometry?.GetType().FullName ?? string.Empty);

            switch (kind)
            {
                case PersistedGeometryKind.Point3d:
                    Point3d point = (Point3d)result.Geometry;
                    writer.SetDouble("PointX", point.X);
                    writer.SetDouble("PointY", point.Y);
                    writer.SetDouble("PointZ", point.Z);
                    break;

                case PersistedGeometryKind.TextEntity:
                case PersistedGeometryKind.GeometryBase:
                    CommonObject commonObject = result.Geometry as CommonObject;
                    if (commonObject != null)
                    {
                        var options = new SerializationOptions();
                        writer.SetString("GeometryJson", commonObject.ToJSON(options));
                    }
                    break;
            }
        }

        private static RhinoResult ReadRhinoResult(GH_IReader reader)
        {
            string layer = string.Empty;
            string lineType = string.Empty;
            string handle = string.Empty;
            string blockName = string.Empty;
            string errorMessage = string.Empty;
            string fileName = string.Empty;

            reader.TryGetString("Layer", ref layer);
            reader.TryGetString("LineType", ref lineType);
            reader.TryGetString("Handle", ref handle);
            reader.TryGetString("BlockName", ref blockName);
            reader.TryGetString("ErrorMessage", ref errorMessage);
            reader.TryGetString("FileName", ref fileName);

            Color color = Color.White;
            reader.TryGetDrawingColor("Color", ref color);

            int geometryKindValue = 0;
            reader.TryGetInt32("GeometryKind", ref geometryKindValue);
            PersistedGeometryKind kind = Enum.IsDefined(typeof(PersistedGeometryKind), geometryKindValue)
                ? (PersistedGeometryKind)geometryKindValue
                : PersistedGeometryKind.None;

            object geometry = null;

            try
            {
                switch (kind)
                {
                    case PersistedGeometryKind.Point3d:
                        geometry = new Point3d(
                            reader.GetDouble("PointX"),
                            reader.GetDouble("PointY"),
                            reader.GetDouble("PointZ"));
                        break;

                    case PersistedGeometryKind.TextEntity:
                    case PersistedGeometryKind.GeometryBase:
                        string geometryJson = reader.GetString("GeometryJson");
                        if (!string.IsNullOrWhiteSpace(geometryJson))
                        {
                            CommonObject commonObject = CommonObject.FromJSON(geometryJson);
                            if (kind == PersistedGeometryKind.TextEntity)
                                geometry = commonObject as TextEntity;
                            else
                                geometry = commonObject as GeometryBase;
                        }
                        break;
                }
            }
            catch
            {
                geometry = null;
                if (string.IsNullOrWhiteSpace(errorMessage))
                    errorMessage = "Failed to restore persisted geometry.";
            }

            return new RhinoResult(geometry, layer, color, lineType, handle, blockName, errorMessage, fileName);
        }

        private static PersistedGeometryKind GetPersistedGeometryKind(object geometry)
        {
            if (geometry == null)
                return PersistedGeometryKind.None;

            if (geometry is TextEntity)
                return PersistedGeometryKind.TextEntity;

            if (geometry is Point3d)
                return PersistedGeometryKind.Point3d;

            if (geometry is GeometryBase)
                return PersistedGeometryKind.GeometryBase;

            return PersistedGeometryKind.None;
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("CDF48638-9C58-4A7E-BD0C-795203527A61"); }
        }




    }

    internal class CButton_Refresh : GH_ComponentAttributes
    {
        public CButton_Refresh(CAD2GH component) : base(component) { }
        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        /// <summary>
        /// 渲染按钮
        /// </summary>
        /// <param name="canvas"></param>
        /// <param name="graphics"></param>
        /// <param name="channel"></param>
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);//定义按钮大小

            if (channel == GH_CanvasChannel.Objects)
            {
                if (((CAD2GH)Owner).CurrentButtonColor == CAD2GH.ButtonColor.Black)
                {
                    using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))//将按钮渲染成黑色
                    {
                        capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                    }
                }
                else
                {
                    using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Grey))//将按钮渲染成灰色
                    {
                        capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                    }
                }
            }

            System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold);
            StringFormat stringFormat = new StringFormat()
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };//指定属性
            graphics.DrawString("Bake", font, Brushes.White, buttonRect, stringFormat);//在按钮上绘制文字
        }
        /// <summary>
        /// 鼠标按下的时候要做的事情
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <returns></returns>
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                CAD2GH info = (CAD2GH)Owner;
                info.CurrentButtonColor = CAD2GH.ButtonColor.Grey;//修改按钮颜色
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                CMath.Delay(50);//暂停50ms，再绘制下一个状态
                info.CurrentButtonColor = CAD2GH.ButtonColor.Black;//修改按钮颜色
                MyBake(info);
                //info.bake = true;
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                //info.bake = false;
                return GH_ObjectResponse.Handled;//结束鼠标事件处理，通知GH已经处理完毕
            }
            return GH_ObjectResponse.Ignore;//若上述条件未满足，则直接返回“未处理”
        }


 
        private void MyBake(CAD2GH info)
        {
            string layerName = info.layerName;
            System.Drawing.Color layerColor = System.Drawing.Color.Black;

            int layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName, -1);

            if (layerIndex == -1)
            {
                layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.Add(layerName, layerColor);
            }

            int count = info.theBakeGeoList.Count;

            List<string> errors = new List<string>();

            for (int i = 0; i < count; i++)
            {
                try
                {
                    Guid id = Guid.Empty;
                    var geo = info.theBakeGeoList[i];

                    if (geo is GeometryBase)
                    {
                        id = Rhino.RhinoDoc.ActiveDoc.Objects.Add((GeometryBase)geo);
                    }
                    else if (geo is Point3d)
                    {
                        id = Rhino.RhinoDoc.ActiveDoc.Objects.AddPoint((Point3d)geo);
                    }
                    else if (geo is TextEntity)
                    {
                        id = Rhino.RhinoDoc.ActiveDoc.Objects.AddText((TextEntity)geo);
                    }
                    else if (geo is Circle)
                    {
                        id = Rhino.RhinoDoc.ActiveDoc.Objects.AddCircle((Circle)geo);
                    }
                    else
                    {
                        errors.Add($"[{i}] 跳过类型: {geo?.GetType().Name}");
                        continue;
                    }

                    if (id == Guid.Empty)
                    {
                        errors.Add($"[{i}] Bake失败（ID为空）");
                        continue;
                    }

                    var obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(id);

                    if (obj != null)
                    {
                        obj.Attributes.LayerIndex = layerIndex;
                        obj.CommitChanges();
                    }
                    else
                    {
                        errors.Add($"[{i}] Bake成功但未找到对象");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"[{i}] 异常: {ex.Message}");
                }
            }

            // 🔥 GH气泡提示（直接用！）
            if (errors.Count > 0)
            {
                string msg = string.Join("\n", errors);

                info.AddRuntimeMessage(
                    Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning,
                    msg
                );
            }
        }
    }

}
