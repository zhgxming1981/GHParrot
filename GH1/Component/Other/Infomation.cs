using System;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI;
using Grasshopper.Kernel.Attributes;
using System.Drawing;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace NS_Parrot
{
    internal class CButton_Infomation : GH_ComponentAttributes
    {
        public CButton_Infomation(Infomation component) : base(component) { }
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
                if (((Infomation)Owner).CurrentButtonColor == Infomation.ButtonColor.Black)
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

            Font font = new Font(GH_FontServer.Small, FontStyle.Bold);
            StringFormat stringFormat = new StringFormat()
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };//指定属性
            graphics.DrawString("检测", font, Brushes.White, buttonRect, stringFormat);//在按钮上绘制文字
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
                Infomation info = (Infomation)Owner;
                info.CurrentButtonColor = Infomation.ButtonColor.Grey;//修改按钮颜色
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                CMath.Delay(50);//暂停50ms，再绘制下一个状态
                info.CurrentButtonColor = Infomation.ButtonColor.Black;//修改按钮颜色
                info.ExpireSolution(true);//告诉系统，电池需要重新计算

                MessageBox.Show("插件名称：Parrot幕墙工具箱\n" +
                    "       作者：月光宝盒\n" +
                    "           IP：" + info.lan_ip + "\n" +
                    "       Mac：" + info.mac + "\n" +
                    "       状态：" + info.legality.ToString(), "消息");
                return GH_ObjectResponse.Handled;//结束鼠标事件处理，通知GH已经处理完毕


            }
            return GH_ObjectResponse.Ignore;//若上述条件未满足，则直接返回“未处理”
        }

       


    }




    public class Infomation : GH_Component
    {
        public enum ButtonColor { Black, Grey }//按钮颜色
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;//当前的按钮颜色


        /// <summary>
        /// Initializes a new instance of the MacOfLAN class.
        /// </summary>
        public Infomation()
          : base("信息", "信息",
              "提供一些系统信息",
              "Parrot", "杂项")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_Infomation(this);
        }
        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("状态", "状态", "当前状态", GH_ParamAccess.item);
            pManager.AddTextParameter("LanIP", "LanIP", "服务器的IP地址", GH_ParamAccess.item);
            pManager.AddTextParameter("MAC", "MAC", "本机的MAC地址", GH_ParamAccess.item);
            pManager.AddTextParameter("LocIP", "LocIP", "本机的IP地址", GH_ParamAccess.item);
        }


        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (CurrentButtonColor == ButtonColor.Grey)//暂时状态，直接退出，以节省计算时间
            {
                return;
            }
            legality = CHardware.CheckLegality2();
            DA.SetData(0, legality);

            lan_ip = CHardware.HostIP;
            DA.SetData(1, lan_ip);

            mac = CHardware.GetCurrentMacAddress();
            DA.SetData(2, mac);

            loc_ip = CHardware.GetCurrentIP();
            DA.SetData(3, loc_ip);
        }

        public bool legality;
        public string lan_ip;
        public string loc_ip;
        public string mac;
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
            get { return new Guid("978445D7-C56F-47D2-9D9D-C8FE3AB3E867"); }
        }
    }
}