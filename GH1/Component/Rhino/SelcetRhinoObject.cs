using System;
using System.Collections.Generic;
using CommonFunction.Algorithm;
using System.Drawing;
using System.Windows.Forms;
using CommonFunction.Hardware;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace NS_Parrot
{
    public class SelcetRhinoObject : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SelcetRhinoObject class.
        /// </summary>
        public SelcetRhinoObject()
          : base("选中物件", "选中物件",
              "选中Rhino中的物件",
              "Parrot", "Rhino")
        {
        }


        public enum ButtonColor { Black, Grey }//按钮颜色
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;//当前的按钮颜色
        public List<GH_Guid> guid = new List<GH_Guid>();
        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guid", "Guid", "Rhino中的物件", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }


        public override void CreateAttributes()
        {
            Attributes = new CButton_Select(this);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            guid.Clear();
            if (!DA.GetDataList(0, guid)) { return; }
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
            get { return new Guid("BE8B8CA7-5D6A-45CE-B1E1-27A4FC1A3B96"); }
        }
    }


    internal class CButton_Select : GH_ComponentAttributes
    {
        public CButton_Select(SelcetRhinoObject component) : base(component) { }
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
                if (((SelcetRhinoObject)Owner).CurrentButtonColor == SelcetRhinoObject.ButtonColor.Black)
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
            graphics.DrawString("选中", font, Brushes.White, buttonRect, stringFormat);//在按钮上绘制文字
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
                SelcetRhinoObject info = (SelcetRhinoObject)Owner;
                info.CurrentButtonColor = SelcetRhinoObject.ButtonColor.Grey;//修改按钮颜色
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                CMath.Delay(50);//暂停50ms，再绘制下一个状态
                info.CurrentButtonColor = SelcetRhinoObject.ButtonColor.Black;//修改按钮颜色
                Select(info);
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                return GH_ObjectResponse.Handled;//结束鼠标事件处理，通知GH已经处理完毕
            }
            return GH_ObjectResponse.Ignore;//若上述条件未满足，则直接返回“未处理”
        }


        private void Select(SelcetRhinoObject obj1)
        {
            if (obj1.guid != null)
            {
                int count = obj1.guid.Count;
                for (int i = 0; i < count; i++)
                {
                    Rhino.DocObjects.RhinoObject obj = RhinoDoc.ActiveDoc.Objects.Find(obj1.guid[i].Value);
                    obj.Select(true, true);
                }
            }


        }


    }
}