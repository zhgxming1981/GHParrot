using AutoCAD;
using AutoCADFunction;
using CommonFunction.Algorithm;
using CommonFunction.Hardware;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using parrot.Properties;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using rd = Rhino.NodeInCode;

namespace NS_Parrot
{
    public class CAD2GH : GH_Component
    {
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

        //private List<object> theObjectList = new List<object>();//从CAD中导入的对象
        public List<object> theBakeGeoList = new List<object>();//将要bake的对象
        //private List<string> theLayerNameList = new List<string>();//autoCAD中图层名
        //private List<System.Drawing.Color> theColorList = new List<Color>();//autoCAD中颜色值
        //private List<string> theLineTypeList = new List<string>();//autoCAD中线形
        //private List<string> theCadEntityHandleList = new List<string>();//当前电池中存储的EntityHandle
        //private List<string> theBlockNameList = new List<string>();//当前电池中存储的块名
        //private string theErrorMessage;
        private List<RhinoResult> theRhinoResultList = new List<RhinoResult>();
        public string layerName = "";


        // 🔥 全局去重集合（核心优化）
        HashSet<string> theHandleSet = new HashSet<string>();

        // 错误信息
        string theErrorMessage = "";



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
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>

        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    if (!CHardware.CheckLegality())
        //        return;

        //    theBakeGeoList.Clear();

        //    Point3d insert = Point3d.Origin;
        //    DA.GetData(0, ref insert);

        //    //Plane plane1 = Plane.WorldXY;
        //    Plane plane1 = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);
        //    Plane plane2 = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);
        //    DA.GetData(1, ref plane2);

        //    DA.GetData(2, ref layerName);

        //    // 🔥 统一变换
        //    Transform xform = Transform.PlaneToPlane(plane1, plane2);

        //    List<object> outputList = new List<object>();

        //    foreach (var obj in theObjectList)
        //    {
        //        if (obj is GeometryBase geo)
        //        {
        //            GeometryBase g = geo.Duplicate();
        //            g.Transform(xform);
        //            outputList.Add(g);
        //        }
        //        else if (obj is TextEntity txt)
        //        {
        //            TextEntity t = txt.Duplicate() as TextEntity;
        //            t.Transform(xform);
        //            outputList.Add(t);
        //        }
        //        else if (obj is Point3d pt)
        //        {
        //            Point3d p = pt;
        //            p.Transform(xform);
        //            outputList.Add(p);
        //        }
        //    }

        //    theBakeGeoList.AddRange(outputList);

        //    DA.SetDataList(0, outputList);
        //    DA.SetDataList(1, theLayerNameList);
        //    DA.SetDataList(2, theColorList);
        //    DA.SetDataList(3, theLineTypeList);
        //    DA.SetDataList(4, theCadEntityHandleList);
        //    DA.SetDataList(5, theBlockNameList);
        //}




        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    if (!CHardware.CheckLegality())
        //        return;

        //    theBakeGeoList.Clear();

        //    Point3d insert = Point3d.Origin;
        //    if (!DA.GetData(0, ref insert))
        //        return;

        //    Plane plane1 = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);

        //    Plane plane2 = Plane.WorldXY;
        //    if (!DA.GetData(1, ref plane2))
        //        return;

        //    DA.GetData(2, ref layerName);

        //    Transform xform = Transform.PlaneToPlane(plane1, plane2);

        //    List<object> outputList = new List<object>();

        //    // 🔥 收集错误（来自 RhinoResult）
        //    List<string> errorList = new List<string>();

        //    for (int i = 0; i < theRhinoResultList.Count; i++)
        //    {
        //        var obj = theRhinoResultList[i].Geometry;

        //        try
        //        {
        //            if (obj is GeometryBase geo)
        //            {
        //                GeometryBase g = geo.Duplicate();
        //                g.Transform(xform);
        //                outputList.Add(g);
        //            }
        //            else if (obj is TextEntity txt)
        //            {
        //                TextEntity t = txt.Duplicate() as TextEntity;
        //                t.Transform(xform);
        //                outputList.Add(t);
        //            }
        //            else if (obj is Point3d pt)
        //            {
        //                Point3d p = pt;
        //                p.Transform(xform);
        //                outputList.Add(p);
        //            }
        //            else
        //            {
        //                outputList.Add(null);
        //            }
        //        }
        //        catch
        //        {
        //            outputList.Add(null);
        //        }

        //        // ✅ 从 RhinoResult 同步错误（关键）
        //        if (i < theRhinoResultList.Count)
        //        {
        //            var rr = theRhinoResultList[i];

        //            if (!string.IsNullOrEmpty(rr.ErrorMessage))
        //            {
        //                string handle = rr.Handle ?? "未知";
        //                errorList.Add($"Handle={handle} : {rr.ErrorMessage}");
        //            }
        //        }
        //    }

        //    theBakeGeoList.AddRange(outputList);

        //    //============================
        //    // 🔥 GH 气泡提示
        //    //============================
        //    if (errorList.Count > 0)
        //    {
        //        string msg = string.Join("\n", errorList);

        //        this.AddRuntimeMessage(
        //            GH_RuntimeMessageLevel.Warning,
        //            msg
        //        );
        //    }

        //    //============================
        //    // 输出
        //    //============================

        //    List<string> theLayerNameList=theRhinoResultList.Select(r => r.Layer).ToList();
        //    List<System.Drawing.Color> theColorList=theRhinoResultList.Select(r => r.Color).ToList();
        //    List<string> theLineTypeList=theRhinoResultList.Select(r => r.LineType).ToList();
        //    List<string> theCadEntityHandleList=theRhinoResultList.Select(r => r.Handle).ToList();
        //    List<string> theBlockNameList=theRhinoResultList.Select(r => r.BlockName).ToList();

        //    DA.SetDataList(0, outputList);
        //    DA.SetDataList(1, theLayerNameList);
        //    DA.SetDataList(2, theColorList);
        //    DA.SetDataList(3, theLineTypeList);
        //    DA.SetDataList(4, theCadEntityHandleList);
        //    DA.SetDataList(5, theBlockNameList);
        //}

        //protected override void SolveInstance(IGH_DataAccess DA)
        //{
        //    if (!CHardware.CheckLegality())
        //        return;

        //    theBakeGeoList.Clear();

        //    Point3d insert = Point3d.Origin;
        //    DA.GetData(0, ref insert);

        //    Plane plane1 = new Plane(insert, Vector3d.XAxis, Vector3d.YAxis);

        //    Plane plane2 = Plane.WorldXY;
        //    if (!DA.GetData(1, ref plane2))
        //        return;

        //    DA.GetData(2, ref layerName);

        //    Transform xform = Transform.PlaneToPlane(plane1, plane2);

        //    List<object> outputList = new List<object>();
        //    List<string> layerList = new List<string>();
        //    List<System.Drawing.Color> colorList = new List<System.Drawing.Color>();
        //    List<string> lineTypeList = new List<string>();
        //    List<string> handleList = new List<string>();
        //    List<string> blockNameList = new List<string>();

        //    List<string> errorList = new List<string>();

        //    foreach (var r in theRhinoResultList)
        //    {
        //        object geoOut = null;

        //        try
        //        {
        //            if (r.Geometry is GeometryBase geo)
        //            {
        //                GeometryBase g = geo.Duplicate();
        //                g.Transform(xform);
        //                geoOut = g;
        //            }
        //            else if (r.Geometry is TextEntity txt)
        //            {
        //                TextEntity t = txt.Duplicate() as TextEntity;
        //                t.Transform(xform);
        //                geoOut = t;
        //            }
        //            else if (r.Geometry is Point3d pt)
        //            {
        //                Point3d p = pt;
        //                p.Transform(xform);
        //                geoOut = p;
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            errorList.Add($"Handle={r.Handle} 变换失败: {ex.Message}");
        //        }

        //        outputList.Add(geoOut);
        //        layerList.Add(r.Layer);
        //        colorList.Add(r.Color);
        //        lineTypeList.Add(r.LineType);
        //        handleList.Add(r.Handle);
        //        blockNameList.Add(r.BlockName);

        //        // ✅ 来自 CAD 层错误
        //        if (!string.IsNullOrEmpty(r.ErrorMessage))
        //        {
        //            errorList.Add($"Handle={r.Handle} : {r.ErrorMessage}");
        //        }
        //    }

        //    theBakeGeoList.AddRange(outputList);

        //    // 🔥 GH 气泡
        //    if (errorList.Count > 0)
        //    {
        //        AddRuntimeMessage(
        //            GH_RuntimeMessageLevel.Warning,
        //            string.Join("\n", errorList)
        //        );
        //    }

        //    DA.SetDataList(0, outputList);
        //    DA.SetDataList(1, layerList);
        //    DA.SetDataList(2, colorList);
        //    DA.SetDataList(3, lineTypeList);
        //    DA.SetDataList(4, handleList);
        //    DA.SetDataList(5, blockNameList);
        //}



        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

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

                outputList.Add(outGeo);
                layerList.Add(r.Layer);
                colorList.Add(r.Color);
                lineTypeList.Add(r.LineType);
                handleList.Add(r.Handle);
                blockNameList.Add(r.BlockName);

                // ✅ 来自 CAD 转换阶段的错误
                if (!string.IsNullOrEmpty(r.ErrorMessage))
                {
                    errorList.Add($"Handle={r.Handle} : {r.ErrorMessage}");
                }
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

        //void GetEntityFromAutoCAD(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        //{
        //    List<RhinoResult> value = AutoCADTool.CAD2Rhino();//就是刚刚写的CAD2Rhino()
        //    theObjectList = value.Geometry;//全局变量，返回的autoCAD对象，类型为List<object> 
        //    theLayerNameList = value.Layer;//全局变量，返回的autoCAD对象图层，类型为List<string>
        //    theColorList = value.Color;//全局变量，返回的autoCAD对象颜色，类型为List<Drawing.Color>
        //    theLineTypeList = value.LineType;//全局变量，返回的autoCAD对象线型，类型为List<string>
        //    theCadEntityHandleList = value.Handle;//全局变量，返回的autoCAD对象Handle，类型为List<string>
        //    theBlockNameList = value.BlockName;//全局变量，返回的autoCAD对象Handle，类型为List<string>
        //    theErrorMessage = value.ErrorMessage;
        //    ExpireSolution(true);//告诉系统，电池需要重新计算
        //}

        //void GetEntityFromAutoCAD(object sender, EventArgs e)
        //{
        //    theRhinoResultList = AutoCADTool.CAD2Rhino();
        //    ExpireSolution(true);
        //}


        void GetEntityFromAutoCAD(object sender, EventArgs e)
        {
            AutoCADTool.CAD2Rhino((res) =>
            {
                theRhinoResultList = res;

                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    ExpireSolution(true);
                }));
            });
        }

        //void AddEntity(object sender, EventArgs e)
        //{
        //    List<RhinoResult> value = AutoCADTool.CAD2Rhino();

        //    theErrorMessage = "";

        //    foreach (var v in value)
        //    {
        //        string handle = v.Handle;

        //        if (string.IsNullOrEmpty(handle))
        //            continue;

        //        // ✅ O(1) 查重
        //        if (theHandleSet.Add(handle)) // ⭐关键写法（Add 本身就返回是否成功）
        //        {
        //            theRhinoResultList.Add(v);
        //        }

        //        // 收集错误
        //        if (!string.IsNullOrEmpty(v.ErrorMessage))
        //        {
        //            theErrorMessage += v.ErrorMessage + "\n";
        //        }
        //    }

        //    ExpireSolution(true);
        //}

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

                // ✅ GH必须在UI线程刷新
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    ExpireSolution(true);
                }));
            });
        }

        //void AddEntity(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        //{
        //    // 获取 CAD 当前选择集的对象
        //    var value = AutoCADTool.CAD2Rhino(); // 返回 (List<object>, List<string>, List<Color>, List<string>)
        //    var newObjects = value.Item1;
        //    var newLayers = value.Item2;
        //    var newColors = value.Item3;
        //    var newLineTypes = value.Item4;
        //    var newHandles = value.Item5;
        //    var newBlockNames = value.Item6;

        //    // 遍历新对象，判断 handle 是否已经存在
        //    for (int i = 0; i < newObjects.Count; i++)
        //    {
        //        string handle = newHandles[i];

        //        // 如果全局 handle 列表中不存在，则添加
        //        if (!theCadEntityHandleList.Contains(handle))
        //        {
        //            theObjectList.Add(newObjects[i]);
        //            theLayerNameList.Add(newLayers[i]);
        //            theColorList.Add(newColors[i]);
        //            theLineTypeList.Add(newLineTypes[i]);
        //            theCadEntityHandleList.Add(handle);
        //            theBlockNameList.Add(newBlockNames[i]);
        //        }
        //        // 否则跳过，不添加
        //    }

        //    // 通知系统需要重新计算
        //    ExpireSolution(true);
        //}


        //void RemoveEntity(object sender, EventArgs e)
        //{
        //    List<RhinoResult> value = AutoCADTool.CAD2Rhino();

        //    // 🔥 要删除的 handle
        //    HashSet<string> removeSet = new HashSet<string>();

        //    foreach (var v in value)
        //    {
        //        if (!string.IsNullOrEmpty(v.Handle))
        //            removeSet.Add(v.Handle);
        //    }

        //    theErrorMessage = "";

        //    // 🔥 重建（O(n)）
        //    List<RhinoResult> newList = new List<RhinoResult>();
        //    HashSet<string> newHandleSet = new HashSet<string>();

        //    foreach (var r in theRhinoResultList)
        //    {
        //        if (!removeSet.Contains(r.Handle))
        //        {
        //            newList.Add(r);
        //            newHandleSet.Add(r.Handle);
        //        }
        //    }

        //    // 替换
        //    theRhinoResultList = newList;
        //    theHandleSet = newHandleSet;

        //    // 收集错误
        //    foreach (var v in value)
        //    {
        //        if (!string.IsNullOrEmpty(v.ErrorMessage))
        //        {
        //            theErrorMessage += v.ErrorMessage + "\n";
        //        }
        //    }

        //    ExpireSolution(true);
        //}


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

                // ✅ GH UI线程刷新
                Rhino.RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    ExpireSolution(true);
                }));
            });
        }
        //void RemoveEntity(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        //{
        //    // 获取 CAD 当前选择集的对象
        //    var value = AutoCADTool.CAD2Rhino(); // 返回 (List<object>, List<string>, List<Color>, List<string>)
        //    var removeObjects = value.Item1;
        //    var removeLayers = value.Item2;
        //    var removeColors = value.Item3;
        //    var removeLineTypes = value.Item4;
        //    var removeHandles = value.Item5;
        //    var removeBlockNames = value.Item6;
        //    // 遍历要移除的 handle 列表
        //    for (int i = 0; i < removeHandles.Count; i++)
        //    {
        //        string handle = removeHandles[i];

        //        // 查找全局列表中 handle 的索引
        //        int index = theCadEntityHandleList.IndexOf(handle);

        //        if (index >= 0)
        //        {
        //            // 从各个全局列表中移除对应元素，保持索引对应
        //            theObjectList.RemoveAt(index);
        //            theLayerNameList.RemoveAt(index);
        //            theColorList.RemoveAt(index);
        //            theLineTypeList.RemoveAt(index);
        //            theCadEntityHandleList.RemoveAt(index);
        //            theBlockNameList.RemoveAt(index);
        //        }
        //        // 如果 handle 不存在全局列表中，则跳过，不报错
        //    }

        //// 通知系统需要重新计算
        //ExpireSolution(true);
        //}

        void ClearEntity(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            //theObjectList.Clear();
            //theLayerNameList.Clear();
            //theColorList.Clear();
            //theLineTypeList.Clear();
            //theCadEntityHandleList.Clear();
            //theBlockNameList.Clear();
            theRhinoResultList.Clear();
            ExpireSolution(true);//告诉系统，电池需要重新计算
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


        private void MyBake2(CAD2GH info)
        {
            string layerName = info.layerName;
            System.Drawing.Color layerColor = System.Drawing.Color.Black;
            int layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.FindByFullPath(layerName, -1);//查找图层的索引号
            if (layerIndex == -1)//如果图层不存在，就新建图层
            {
                layerIndex = Rhino.RhinoDoc.ActiveDoc.Layers.Add(layerName, layerColor);
            }
            int count = info.theBakeGeoList.Count;

            for (int i = 0; i < count; i++)
            {
                Guid id = new Guid();

                if (info.theBakeGeoList[i] is GeometryBase)
                {
                    id = Rhino.RhinoDoc.ActiveDoc.Objects.Add((GeometryBase)info.theBakeGeoList[i]);//将几何体加入到Rhino文档中
                }
                else if (info.theBakeGeoList[i] is Point3d)
                {
                    id = Rhino.RhinoDoc.ActiveDoc.Objects.AddPoint((Point3d)info.theBakeGeoList[i]);
                }
                else if (info.theBakeGeoList[i] is TextEntity)
                {
                    id = Rhino.RhinoDoc.ActiveDoc.Objects.AddText((TextEntity)info.theBakeGeoList[i]);
                }
                else if (info.theBakeGeoList[i] is Circle)
                {
                    id = Rhino.RhinoDoc.ActiveDoc.Objects.AddCircle((Circle)info.theBakeGeoList[i]);
                }
                else
                {
                    MessageBox.Show("第" + i.ToString() + "个元素不可bake，类型为：" + info.theBakeGeoList[i].ToString());
                    return;
                }
                Rhino.DocObjects.RhinoObject obj = Rhino.RhinoDoc.ActiveDoc.Objects.FindId(id);
                obj.Attributes.LayerIndex = layerIndex;
                obj.CommitChanges();//重要，否则看不到任何效果   
            }
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