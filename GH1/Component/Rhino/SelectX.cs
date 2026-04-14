using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using CommonFunction.Hardware;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Microsoft.Office.Interop.Excel;
//using NS_Parrot.Properties;
using Rhino;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class SelectX : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SelectX class.
        /// </summary>
        public SelectX()
          : base("SelectX", "SelectX",
              "增强选择",
              "Parrot", "Rhino")
        {
        }

        List<GH_Guid> TheGuid = new List<GH_Guid>();

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Guid", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            //DA.GetDataList(0, TheGuid);

            DA.SetDataList(0, TheGuid);
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



        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            ToolStripMenuItem item0 = new ToolStripMenuItem();
            item0.Text = "加选";
            //item0.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item0);
            item0.Click += AddSelect;

            ToolStripMenuItem item1 = new ToolStripMenuItem();
            item1.Text = "减选";
            //item1.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item1);
            item1.Click += SubSelect;

            ToolStripMenuItem item2 = new ToolStripMenuItem();
            item2.Text = "交集";
            //item2.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item2);
            item2.Click += Intersect;

            ToolStripMenuItem item3 = new ToolStripMenuItem();
            item3.Text = "清空";
            //item3.Image = Resources.check.GetThumbnailImage(25, 25, null, IntPtr.Zero); // 自定义的图片, Bitmap类型转Image
            menu.Items.Add(item3);
            item3.Click += ClearData;
        }

        void AddSelect(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            Rhino.DocObjects.ObjRef[] rhObjects;
            Rhino.Input.RhinoGet.GetMultipleObjects("选择物体：\n", true, null, out rhObjects);
            if (rhObjects == null) return;
            int count = rhObjects.Length;
            List<GH_Guid> s1 = new List<GH_Guid>(count);
            for (int i = 0; i < count; i++)
            {
                s1.Add(new GH_Guid(rhObjects[i].ObjectId));
            }
            TheGuid = s1.Union(TheGuid, new RhionObjectCompare()).ToList();//求并集
            ExpireSolution(true);//告诉系统，电池需要重新计算
        }

        void SubSelect(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            Rhino.DocObjects.ObjRef[] rhObjects;
            Rhino.Input.RhinoGet.GetMultipleObjects("选择物体：\n", true, null, out rhObjects);
            if (rhObjects == null) return;
            int count = rhObjects.Length;
            List<GH_Guid> s1 = new List<GH_Guid>(count);
            for (int i = 0; i < count; i++)
            {
                s1.Add(new GH_Guid(rhObjects[i].ObjectId));
            }

            TheGuid = TheGuid.Except(s1, new RhionObjectCompare()).ToList();//求差集
            ExpireSolution(true);//告诉系统，电池需要重新计算
        }


        void Intersect(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            Rhino.DocObjects.ObjRef[] rhObjects;
            Rhino.Input.RhinoGet.GetMultipleObjects("选择物体：\n", true, null, out rhObjects);
            if (rhObjects == null) return;
            int count = rhObjects.Length;
            List<GH_Guid> s1 = new List<GH_Guid>(count);
            for (int i = 0; i < count; i++)
            {
                s1.Add(new GH_Guid(rhObjects[i].ObjectId));
            }

            TheGuid = TheGuid.Intersect(s1, new RhionObjectCompare()).ToList();//求交集
            ExpireSolution(true);//告诉系统，电池需要重新计算
        }

        void ClearData(object argumentNameIsNotImportentEither, EventArgs butTheirOrderMatters)
        {
            TheGuid.Clear();
            ExpireSolution(true);//告诉系统，电池需要重新计算
        }


        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("C90BE016-9F58-412B-B39E-2029817B361E"); }
        }
    }
}