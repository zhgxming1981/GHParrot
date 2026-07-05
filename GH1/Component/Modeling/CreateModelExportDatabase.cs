using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CreateModelExportDatabase : GH_Component
    {
        public bool RunNow { get; set; }

        private string _lastPath = string.Empty;
        private string _status = "未创建。";

        public CreateModelExportDatabase()
          : base("创建模型导出数据库", "模型导出库",
              "创建导出.3dm 和筛选重复导出使用的 SQLite 数据库",
              "Parrot", "Modeling")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件夹", "文件夹", "数据库文件夹路径。", GH_ParamAccess.item);
            pManager.AddTextParameter("数据库名称", "数据库名称", "数据库文件名，不带扩展名时自动追加 .db。", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("数据库地址", "数据库地址", "创建后的数据库完整路径。", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "Status", "创建状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string folder = string.Empty;
            string databaseName = string.Empty;

            DA.GetData(0, ref folder);
            DA.GetData(1, ref databaseName);

            if (RunNow)
            {
                RunNow = false;
                try
                {
                    _lastPath = CreateDatabase(folder, databaseName);
                    _status = "创建完成：" + _lastPath;
                }
                catch (Exception ex)
                {
                    _status = "创建失败：" + ex.Message;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _status);
                }
            }

            DA.SetData(0, _lastPath);
            DA.SetData(1, _status);
        }

        public override void CreateAttributes()
        {
            Attributes = new CreateModelExportDatabaseAttributes(this);
        }

        private static string CreateDatabase(string folder, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("数据库文件夹不能为空。");
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentException("数据库名称不能为空。");

            string fullFolder = Path.GetFullPath(folder.Trim());
            Directory.CreateDirectory(fullFolder);

            string fileName = databaseName.Trim();
            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("数据库名称必须是文件名，不能是路径。");

            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                fileName += ".db";

            string fullPath = Path.GetFullPath(Path.Combine(fullFolder, fileName));
            ModelExportRecordStore store = new ModelExportRecordStore(fullPath);
            store.EnsureSchema();
            return fullPath;
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_CreateBrepCodeDatabase"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("BB44B349-07F5-4D5E-8D61-781CDE64BC10"); }
        }
    }

    internal class CreateModelExportDatabaseAttributes : GH_ComponentAttributes
    {
        public CreateModelExportDatabaseAttributes(CreateModelExportDatabase owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 22.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = GetButtonRect();
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && GetButtonRect().Contains(e.CanvasLocation))
            {
                CreateModelExportDatabase component = (CreateModelExportDatabase)Owner;
                component.RunNow = true;
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }

        private RectangleF GetButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }
    }
}
