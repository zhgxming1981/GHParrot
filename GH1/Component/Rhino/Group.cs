using CommonFunction.Hardware;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NS_Parrot
{
    public class Group : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Group class.
        /// </summary>
        public Group()
          : base("Group", "Group",
              "提取组内对象",
              "Parrot", "Rhino")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Data", "tree", "D(T)", GH_ParamAccess.tree);
            pManager.AddGenericParameter("Data2", "tree2", "D(T)", GH_ParamAccess.tree);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<GH_Guid> gh_guid_list = new List<GH_Guid>();
            retVal_group.Clear();
            retVal_group2.Clear();

            if (!DA.GetDataList(0, gh_guid_list)) { return; }

            List<Guid> guid_list1 = new List<Guid>();
            List<Guid> guid_list2 = new List<Guid>();
            foreach (GH_Guid item in gh_guid_list)
            {
                guid_list1.Add(item.Value);
                guid_list2.Add(item.Value);
            }

            ClassifyGroup_List(guid_list1);
            DA.SetDataTree(0, retVal_group);
            DA.SetDataTree(1, retVal_group2);
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("AEF01E28-055F-4380-840C-96EDF9AA03B7"); }
        }


        private DataTree<GeometryBase> retVal_group = new DataTree<GeometryBase>();//返回值

        private DataTree<RhinoObject> retVal_group2 = new DataTree<RhinoObject>();//返回值


        /// <summary>
        /// 找到那些元素不在组内，从id_list中删除，并将这些元素加入到geo中
        /// </summary>
        /// <param name="id_list"></param>
        /// <returns></returns>
        private void ClassifyGroup_List(List<Guid> guid_list)
        {
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.DocObjects.Tables.GroupTable tab = doc.Groups;
            int pathNO = 0;

            int len_tab = tab.Count;
            for (int i = 0; i < len_tab; i++)
            {
                var member_tab = tab.GroupMembers(i);
                bool canIncrease = false;
                foreach (var member in member_tab)
                {
                    canIncrease = false;
                    for (int j = 0; j < guid_list.Count; j++)
                    {
                        if (member.Id == guid_list[j])
                        {
                            Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(pathNO);
                            retVal_group.Add(member.Geometry, path);
                            guid_list.RemoveAt(j);
                            canIncrease = true;
                        }
                    }
                }
                if (canIncrease)//保证pathNO按顺序增加，不跳号，跳号有时候会让某些程序产生莫名的错误
                {
                    pathNO++;
                }
            }

            for (int i = 0; i < guid_list.Count; i++)//把剩下不在组里的物件装进retVal_list中
            {
                Grasshopper.Kernel.Data.GH_Path path = new Grasshopper.Kernel.Data.GH_Path(pathNO++);
                retVal_group.Add(doc.Objects.FindGeometry(guid_list[i]), path);
            }
        }




        /// <summary>
        /// 判断对象在哪个组中，不在任何组中返回-1
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>所在组的索引号
        private List<int> IsInGroup(Guid item)
        {
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.DocObjects.Tables.GroupTable tab = doc.Groups;
            int len_tab = tab.Count;
            List<int> retVal = new List<int>();
            for (int i = 0; i < len_tab; i++)
            {
                var member_tab = tab.GroupMembers(i);
                foreach (var member in member_tab)
                {
                    {
                        if (member.Id == item)
                        {
                            retVal.Add(i);
                            break;
                        }
                    }
                }
            }
            return retVal;
        }

    }

    class MyGroup
    {
        public int GroupIndex = 0;
        public List<int> all_GroupIndex = new List<int>();
        public List<Guid> Guid_list
        {
            get
            { return guid_list; }
        }
        public List<Guid> guid_list = new List<Guid>();
        public IEnumerable<int> SubGroupIndex
        {
            get
            {
                return subGroupIndex;
            }
        }
        private IEnumerable<int> subGroupIndex = new List<int>();
        private DataTree<RhinoObject> rhino_objects = new DataTree<RhinoObject>();
        public DataTree<RhinoObject> Rhino_Objects
        {
            get { return rhino_objects; }
        }

        private GH_Path gh_path = new GH_Path(0);


        public MyGroup(List<int> all_GroupIndex)
        {
            this.all_GroupIndex = all_GroupIndex;
            for (int i = 0; i < all_GroupIndex.Count; i++)
            {
                SetMyGroup(all_GroupIndex[i], gh_path);
            }
        }


        private bool InTheDataTree(RhinoObject guid)
        {
            int count = rhino_objects.BranchCount;

            for (int i = 0; i < count; i++)
            {
                foreach (RhinoObject item in rhino_objects.Branches[i])
                {
                    if (item.Id == guid.Id)
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        /// <summary>
        /// 生成组的包含关系
        /// </summary>
        /// <param name="outermost_groupIndex"></param>最外层组索引
        public void SetMyGroup(int outermost_groupIndex, GH_Path path)
        {
            //删掉当前的组号，因为已经处理过，这是为了防止一个组被重复计算
            this.all_GroupIndex.Remove(outermost_groupIndex);
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.DocObjects.Tables.GroupTable tab = doc.Groups;
            int len_tab = tab.Count;
            for (int i = 0; i < len_tab; i++)
            {
                var member_tab = tab.GroupMembers(outermost_groupIndex);
                foreach (var member in member_tab)
                {
                    if (InTheDataTree(member))
                    {
                        return;//每个元素只处理一次
                    }

                    List<int> temp = IsInGroup(member.Id);//组号
                    subGroupIndex = subGroupIndex.Union(temp);
                    int temp_count = temp.Count;
                    if (temp_count == 1)//只在当前组中
                    {
                        guid_list.Add(member.Id);
                        path = path.Increment(path.Length - 1, 1);//在本路径的序号上加1
                        rhino_objects.Add(member, path);
                    }
                    else if (temp_count > 1)
                    {
                        SortTheGroup(temp);
                        for (int j = 0; j < temp_count; j++)
                        {
                            path = path.AppendElement(1);//增加下一级路径
                            SetMyGroup(temp[j], path);
                        }
                    }
                }
            }
        }



        /// <summary>
        /// 返回一个对象所属的组索引号，它可能属于多个组
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        private List<int> IsInGroup(Guid item)
        {
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.DocObjects.Tables.GroupTable tab = doc.Groups;
            int len_tab = tab.Count;
            List<int> retVal = new List<int>();
            for (int i = 0; i < len_tab; i++)
            {
                var member_tab = tab.GroupMembers(i);
                foreach (var member in member_tab)
                {
                    {
                        if (member.Id == item)
                        {
                            retVal.Add(i);
                            break;
                        }
                    }
                }
            }
            return retVal;
        }


        /// <summary>
        /// 根据元素多少排序Group，少的排前面，多的排后面。元素多的包含元素少的
        /// </summary>
        /// <param name="index_list"></param>
        private void SortTheGroup(List<int> index_list)
        {
            int count = index_list.Count;
            Rhino.RhinoDoc doc = Rhino.RhinoDoc.ActiveDoc;
            Rhino.DocObjects.Tables.GroupTable tab = doc.Groups;

            for (int i = 0; i < count - 1; i++)
            {
                int index_i = index_list[i];
                int min_count = tab.GroupMembers(index_i).Length;
                for (int j = i + 1; j < count; j++)
                {
                    int index_j = index_list[j];
                    int count_j = tab.GroupMembers(index_j).Length;
                    if (count_j < min_count)
                    {
                        min_count = count_j;
                        int m = index_list[i];
                        index_list[i] = index_list[j];
                        index_list[j] = m;
                    }
                }
            }
        }
    }
}