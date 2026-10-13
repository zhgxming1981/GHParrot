using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 仅在收到刷新触发时，才将当前输入数据转发给下游。
    /// </summary>
    public class DataRefresh : GH_Component
    {
        private GH_Structure<IGH_Goo> _cachedData = new GH_Structure<IGH_Goo>();
        private bool _lastRefresh;
        private bool _refreshPending;
        private GH_Canvas _subscribedCanvas;

        public DataRefresh()
          : base("数据刷新", "数据刷新",
              "布尔从 False 切换为 True，或点击组件上的刷新按钮时，更新并输出当前数据。未刷新时保持上一次输出。",
              "Parrot", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "数据", "需要按需刷新并转发的数据，支持 item、list、tree。", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("刷新", "刷新", "从 False 切换为 True 时刷新数据。", GH_ParamAccess.item, false);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "数据", "最近一次刷新时缓存的数据。", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Structure<IGH_Goo> inputData;
            DA.GetDataTree(0, out inputData);

            bool refresh = false;
            DA.GetData(1, ref refresh);

            bool shouldRefresh = _refreshPending || (refresh && !_lastRefresh);
            if (shouldRefresh)
            {
                _cachedData = DuplicateTree(inputData);
                _refreshPending = false;
            }

            DA.SetDataTree(0, DuplicateTree(_cachedData));
            _lastRefresh = refresh;
        }

        public void RefreshFromButton()
        {
            _refreshPending = true;
            ExpireSolution(true);
        }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            SubscribeCanvasF5();
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            UnsubscribeCanvasF5();
            base.RemovedFromDocument(document);
        }

        private void SubscribeCanvasF5()
        {
            GH_Canvas canvas = Grasshopper.Instances.ActiveCanvas;
            if (canvas == null || _subscribedCanvas == canvas)
                return;

            UnsubscribeCanvasF5();
            _subscribedCanvas = canvas;
            _subscribedCanvas.KeyDown += OnCanvasKeyDown;
        }

        private void UnsubscribeCanvasF5()
        {
            if (_subscribedCanvas == null)
                return;

            _subscribedCanvas.KeyDown -= OnCanvasKeyDown;
            _subscribedCanvas = null;
        }

        private void OnCanvasKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
                _refreshPending = true;
        }

        private static GH_Structure<IGH_Goo> DuplicateTree(GH_Structure<IGH_Goo> source)
        {
            GH_Structure<IGH_Goo> result = new GH_Structure<IGH_Goo>();
            if (source == null)
                return result;

            foreach (GH_Path path in source.Paths)
            {
                foreach (IGH_Goo item in source.get_Branch(path))
                    result.Append(item?.Duplicate(), path);
            }

            return result;
        }

        public override void CreateAttributes()
        {
            Attributes = new DataRefreshAttributes(this);
        }

        protected override Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("C6A19CE3-06D3-48F9-BBE9-052D5C35315D"); }
        }
    }

    internal class DataRefreshAttributes : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public DataRefreshAttributes(DataRefresh component) : base(component)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight);
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

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            })
            {
                graphics.DrawString("刷新", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && GetButtonRect().Contains(e.CanvasLocation))
            {
                ((DataRefresh)Owner).RefreshFromButton();
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }

        private RectangleF GetButtonRect()
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - ButtonHeight, Bounds.Width, ButtonHeight);
            buttonRect.Inflate(-5.0f, -2.0f);
            return buttonRect;
        }
    }
}
