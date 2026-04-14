using CommonFunction.Algorithm;
using CommonFunction.Hardware;
using ExcelFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class Gh2Excel : GH_Component//, IGH_VariableParameterComponent
    {
        /// <summary>
        /// Initializes a new instance of the Gh2Excel class.
        /// </summary>
        public Gh2Excel()
          : base("GH2Excel", "GH2Excel",
              "将GH中的数据写入Excel中",
              "Parrot", "ExcelCAD")
        {
        }

        public enum ButtonColor { Black, Grey }//按钮颜色
        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;//当前的按钮颜色

        public bool WriteNow = false;

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Path", "Path", "文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("Sheet", "Sheet", "SheetName", GH_ParamAccess.item);
            pManager.AddTextParameter("Title", "表头", "表头，以列表的格式提供，可以为空", GH_ParamAccess.list);
            pManager.AddTextParameter("Start", "起始", "起始位置", GH_ParamAccess.item);
            pManager.AddTextParameter("Data", "Data", "要写入Excel的数据，先要格式化，变成A|B|C的格式", GH_ParamAccess.list);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddNumberParameter("参数个数", "参数个数", "参数个数", GH_ParamAccess.item);
        }


        public override void CreateAttributes()
        {
            Attributes = new CButton_Write2Excel(this);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            if (!WriteNow) return;

            string fileName = "";
            if (!DA.GetData(0, ref fileName)) { return; }

            string sheetName = "";
            if (!DA.GetData(1, ref sheetName)) { return; }

            List<string> title2 = new List<string>();
            DA.GetDataList(2, title2);

            List<string> title = new List<string>();
            if (title2.Count > 0)
            {
                title.Add(FormatTitle(title2));
            }

            string start = "";
            if (!DA.GetData(3, ref start)) { return; }

            List<string> data = new List<string>();
            if (!DA.GetDataList(4, data)) { return; }

            title.AddRange(data);

            MyExcel myExcel = new MyExcel();
            myExcel.GetExcel();
            if (File.Exists(fileName))
            {
                myExcel.OpenExcelFile(fileName);
            }
            else
            {
                myExcel.NewExcelFile();
                myExcel.SaveAsExcelFile(fileName);
            }
            myExcel.SetExcelWindowState(false, false, false);

            myExcel.SetActiveSheet(sheetName);

            int row = myExcel.Row(start);
            int column = myExcel.Column(start);

            int count_row = title.Count;

            List<List<string>> string_row = new List<List<string>>(count_row);
            for (int i = 0; i < count_row; i++)
            {
                char[] splitChar = { '|' };
                if (title[i] != null)
                {
                    string[] splitTxt = title[i].Split(splitChar);
                    int count_col = splitTxt.Length;
                    List<string> string_col = new List<string>(count_col);
                    for (int j = 0; j < count_col; j++)
                    {
                        string_col.Add(splitTxt[j]);
                    }
                    string_row.Add(string_col);
                }
            }
            myExcel.WriteData(row, column, string_row);
            myExcel.SaveExcelFile();
            myExcel.SetExcelWindowState(true, true, true);
            WriteNow = false;
        }



        string FormatTitle(List<string> title)
        {
            int count = title.Count;
            StringBuilder retval = new StringBuilder();
            for (int i = 0; i < count - 1; i++)
            {
                retval.Append(title[i]);
                retval.Append("|");
            }
            retval.Append(title[count - 1]);
            return retval.ToString();
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
            get { return new Guid("A0501BF7-B2D2-4C03-8BA7-D5D6567E181C"); }
        }






    }

    internal class CButton_Write2Excel : GH_ComponentAttributes
    {
        public CButton_Write2Excel(Gh2Excel component) : base(component) { }
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
                if (((Gh2Excel)Owner).CurrentButtonColor == Gh2Excel.ButtonColor.Black)
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
            graphics.DrawString("写入", font, Brushes.White, buttonRect, stringFormat);//在按钮上绘制文字
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
                Gh2Excel info = (Gh2Excel)Owner;
                info.CurrentButtonColor = Gh2Excel.ButtonColor.Grey;//修改按钮颜色
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                CMath.Delay(50);//暂停50ms，再绘制下一个状态
                info.CurrentButtonColor = Gh2Excel.ButtonColor.Black;//修改按钮颜色

                info.WriteNow = true;
                info.ExpireSolution(true);//告诉系统，电池需要重新计算
                return GH_ObjectResponse.Handled;//结束鼠标事件处理，通知GH已经处理完毕
            }
            return GH_ObjectResponse.Ignore;//若上述条件未满足，则直接返回“未处理”
        }
    }
}