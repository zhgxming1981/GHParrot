using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Rhino.FileIO;
using System.Windows.Forms;
using System.Linq;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    public class XingCaiLibray : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the XingCaiLibray class.
        /// </summary>
        public XingCaiLibray()
          : base("型材库", "型材库",
              "型材库",
              "Parrot", "库")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件路径", "File", "文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("群组名称", "Name", "群组名称", GH_ParamAccess.item);
            pManager.AddIntegerParameter("插入点编号", "IPN", "插入点编号，由属性SN定义，从0开始", GH_ParamAccess.item, 0);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("截面", "sec", "截面", GH_ParamAccess.list);
            pManager.AddPointParameter("插入点", "IP", "插入点", GH_ParamAccess.item);
            pManager.AddTextParameter("属性", "Att", "截面属性", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string filePath = "";
            if (!DA.GetData(0, ref filePath)) { return; }

            string groupName = "";
            if (!DA.GetData(1, ref groupName)) { return; }

            int insertPoint = 0;
            if (!DA.GetData(2, ref insertPoint)) { return; }



            File3dm file = Rhino.FileIO.File3dm.Read(filePath);
            File3dmGroupTable groupTable = file.AllGroups;
            File3dmLayerTable layerTable = file.AllLayers;

            int count_lay = layerTable.Count;

            List<Curve> curves = new List<Curve>();
            Point insert = new Point(new Point3d());
            List<string> att = new List<string>();
            //att.AddRange();

            Rhino.DocObjects.Group group = groupTable.FindName(groupName);
            if (group != null)
            {
                Rhino.FileIO.File3dmObject[] objs = GetGroupMember(file, group.Index);
                //Rhino.FileIO.File3dmObject[] objs = groupTab.GroupMembers(group.Index);//此函数似乎有bug，引发空值异常
                //Rhino.FileIO.File3dmObject[] objs1 = file.Objects.FindByGroup(group);//此函数似乎有bug，引发空值异常

                foreach (var item in objs)
                {
                    if (item.Geometry.GetType() == typeof(TextEntity) && OnTheLayer(item, layerTable, "$自编模号"))
                    {
                        att.Add("自编模号:" + ((TextEntity)item.Geometry).PlainText);
                    }

                    if (item.Geometry.GetType() == typeof(TextEntity) && OnTheLayer(item, layerTable, "$工厂模号"))
                    {
                        att.Add("工厂模号:" + ((TextEntity)item.Geometry).PlainText);
                    }


                    if (item.Geometry is Curve && OnTheLayer(item, layerTable, "$轮廓线"))
                    {
                        curves.Add((Curve)item.Geometry);
                        int count_user = item.Attributes.GetUserStrings().Count;
                        if (count_user > 0)
                        {
                            System.Collections.Specialized.NameValueCollection userString = item.Attributes.GetUserStrings();

                            int i = 3;
                            foreach (var str in userString)
                            {
                                //string i_txt = (i > 9 ? i.ToString() : "0" + i);//小于9就变成0x
                                att.Add(str + ":" + item.Attributes.GetUserString((string)str));
                                i++;
                            }
                        }
                    }
                    File3dmObject po;
                    if (InsertPointCount(objs, out po) == 1)
                    {
                        insert = (Point)po.Geometry;
                    }
                    else
                    {
                        if (item.Geometry.GetType() == typeof(Point) && GetPointBySN(item, insertPoint.ToString()) && FindLayerByName(layerTable, "$插入点") > 0)
                        {
                            insert = (Point)item.Geometry;
                        }
                    }
                }
            }

            att.Sort();
            DA.SetDataList(0, curves);
            DA.SetData(1, insert.Location);
            DA.SetDataList(2, att);
        }


        /// <summary>
        /// 返回对应名字的图层索引
        /// </summary>
        /// <param name="layerTable"></param>
        /// <param name="layerName"></param>
        /// <returns></returns>
        private int FindLayerByName(File3dmLayerTable layerTable, string layerName)
        {
            int count = layerTable.Count;
            foreach (var item in layerTable)
            {
                if (item.Name == layerName)
                    return item.Index;
            }
            return -1;
        }

        /// <summary>
        /// 判断item是否在layerName图层上
        /// </summary>
        /// <param name="item"></param>要判断的对象
        /// <param name="layerTable"></param>图层表
        /// <param name="layerName"></param>图层名
        /// <returns></returns>
        private bool OnTheLayer(Rhino.FileIO.File3dmObject item, File3dmLayerTable layerTable, string layerName)
        {
            int count = layerTable.Count;
            int layIndex = -1;
            foreach (var lay in layerTable)
            {
                if (lay.Name == layerName)
                {
                    layIndex = lay.Index;
                    break;
                }
            }
            if (item.Attributes.LayerIndex == layIndex)
                return true;
            return false;
        }


        /// <summary>
        /// 返回group_index组内的全部成员
        /// </summary>
        /// <param name="file"></param>在哪个文件力找
        /// <param name="group_index"></param>组index
        /// <returns></returns>
        private Rhino.FileIO.File3dmObject[] GetGroupMember(File3dm file, int group_index)
        {
            List<Rhino.FileIO.File3dmObject> retVal = new List<Rhino.FileIO.File3dmObject>();
            var obj = file.Objects.GetEnumerator();//获取文件所有对象的迭代器
            while (obj.MoveNext())
            {
                var obj1 = obj.Current.Attributes.GetGroupList();//该对象的所有组索引，某些对象会属于1个以上的组
                if (obj1 != null)
                {
                    int count = obj1.Length;
                    foreach (var item in obj1)
                    {
                        if (item == group_index)
                            retVal.Add(obj.Current);
                    }
                }
            }
            return retVal.ToArray();
        }

        /// <summary>
        /// 判断点的userString是否是sn
        /// </summary>
        /// <param name="po"></param>要判断的点
        /// <param name="sn"></param>点的属性（userString）
        /// <returns></returns>
        private bool GetPointBySN(File3dmObject po, string sn)
        {
            if (po.Attributes.GetUserString("SN") == sn)
                return true;
            return false;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return null;
            }
        }


        /// <summary>
        /// 组内有多少个插入点
        /// </summary>
        /// <param name="objs"></param>组的成员
        /// <returns></returns>插入点数量
        private int InsertPointCount(Rhino.FileIO.File3dmObject[] objs, out File3dmObject refPoint)
        {
            int retVal = 0;
            refPoint = null;
            foreach (var item in objs)
            {
                if (item.Geometry.GetType() == typeof(Point))
                {
                    refPoint = item;
                    retVal++;
                }
            }
            if (retVal != 1)
            {
                refPoint = null;
            }
            return retVal;
        }


        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("6747FFED-EE71-49A4-9B8A-BE1BEAAA6656"); }
        }
    }
}