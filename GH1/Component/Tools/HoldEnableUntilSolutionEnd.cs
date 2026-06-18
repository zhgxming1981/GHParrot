using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

namespace NS_Parrot
{
    public class HoldEnableUntilSolutionEnd : GH_Component
    {
        private bool _lastStart;
        private bool _running;
        private bool _autoDisable = true;
        private bool _skipCurrentSolutionEnd;
        private int _remainingCycles;
        private DateTime _startTime;
        private double _lastElapsedSeconds;
        private bool _donePulse;
        private bool _resetDonePulseScheduled;
        private GH_Document _subscribedDocument;
        private readonly List<Guid> _targetIds = new List<Guid>();
        private string _message = "等待启动";

        public HoldEnableUntilSolutionEnd()
          : base("HoldEnableUntilSolutionEnd", "保持启用",
              "把 Data 作为闸门转发到 Control，并通过 Control 连线或 Name 昵称临时启用目标电池，等待指定数量的 Grasshopper 解算结束后自动禁用。",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "Data", "需要转发给下游目标电池的任意数据。未运行时不会读取也不会输出。", GH_ParamAccess.tree);
            pManager.AddBooleanParameter("启动", "Start", "False 变 True 时启动。可以接 Button。", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("解算轮数", "Cycles", "保持启用几个完整 Solution。通常填 1。", GH_ParamAccess.item, 1);
            pManager.AddBooleanParameter("结束禁用", "AutoOff", "完成后是否自动禁用目标。", GH_ParamAccess.item, true);
            pManager.AddTextParameter("目标昵称", "Name", "Control 没有连线时，用这个昵称查找要控制的电池或电池包。", GH_ParamAccess.item);

            pManager[0].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("控制", "Control", "运行时转发 Data；这根输出线也用于指定要启用/禁用的下游目标。", GH_ParamAccess.tree);
            pManager.AddTextParameter("Time", "Time", "上一次目标解算耗时。", GH_ParamAccess.item);
            pManager.AddIntegerParameter("找到数量", "Found", "本次找到的目标数量。", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "Message", "当前状态。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Done", "Done", "目标解算完成后一轮为 True，用于触发下游电池。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool start = false;
            int cycles = 1;
            bool autoDisable = true;
            string targetName = string.Empty;

            DA.GetData(1, ref start);
            DA.GetData(2, ref cycles);
            DA.GetData(3, ref autoDisable);
            DA.GetData(4, ref targetName);

            bool startTriggered = start && !_lastStart;
            _lastStart = start;

            GH_Document document = OnPingDocument();
            if (document == null)
            {
                _message = "没有找到当前 GH 文档";
                SetStatusOutputs(DA);
                return;
            }

            if (startTriggered)
                StartRun(document, targetName, Math.Max(1, cycles), autoDisable);

            if (_running && Params.Input[0].SourceCount > 0)
            {
                GH_Structure<IGH_Goo> dataTree;
                if (DA.GetDataTree(0, out dataTree) && dataTree != null)
                    DA.SetDataTree(0, dataTree);
            }

            SetStatusOutputs(DA);

            if (_donePulse && !_resetDonePulseScheduled)
            {
                _resetDonePulseScheduled = true;
                document.ScheduleSolution(1, doc =>
                {
                    _donePulse = false;
                    _resetDonePulseScheduled = false;
                    ExpireSolution(false);
                });
            }
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            UnsubscribeDocument();
            base.RemovedFromDocument(document);
        }

        private void StartRun(GH_Document document, string targetName, int cycles, bool autoDisable)
        {
            List<IGH_DocumentObject> targets = FindTargets(document, targetName);
            _targetIds.Clear();
            _targetIds.AddRange(targets.Select(item => item.InstanceGuid));

            if (targets.Count == 0)
            {
                _running = false;
                _message = string.IsNullOrWhiteSpace(targetName)
                    ? "Control 没有连接到目标，也没有填写 Name"
                    : "没有找到昵称为 \"" + targetName + "\" 的目标";
                return;
            }

            _running = true;
            _autoDisable = autoDisable;
            _remainingCycles = cycles;
            _skipCurrentSolutionEnd = true;
            _startTime = DateTime.Now;
            _lastElapsedSeconds = 0.0;
            _donePulse = false;
            _resetDonePulseScheduled = false;
            SubscribeDocument(document);
            _message = "准备启用 " + targets.Count + " 个目标，等待 " + cycles + " 轮解算结束";

            document.ScheduleSolution(1, doc =>
            {
                List<IGH_DocumentObject> scheduledTargets = FindTargetsById(doc, _targetIds);
                SetObjectsEnabled(scheduledTargets, true, true);
                ExpireSolution(false);
            });
        }

        private void OnSolutionEnd(object sender, GH_SolutionEventArgs e)
        {
            if (!_running)
                return;

            GH_Document document = sender as GH_Document;
            if (document == null)
                return;

            if (_skipCurrentSolutionEnd)
            {
                _skipCurrentSolutionEnd = false;
                return;
            }

            _remainingCycles--;
            if (_remainingCycles > 0)
            {
                _message = "目标保持启用，剩余 " + _remainingCycles + " 轮解算";
                document.ScheduleSolution(1, doc => ExpireSolution(false));
                return;
            }

            _lastElapsedSeconds = (DateTime.Now - _startTime).TotalSeconds;
            _running = false;
            UnsubscribeDocument();

            document.ScheduleSolution(1, doc =>
            {
                List<IGH_DocumentObject> targets = FindTargetsById(doc, _targetIds);
                if (_autoDisable)
                    SetObjectsEnabled(targets, false, false);

                _message = _autoDisable
                    ? "解算结束，已禁用 " + targets.Count + " 个目标"
                    : "解算结束，目标保持启用";
                _donePulse = true;
                _resetDonePulseScheduled = false;
                ExpireSolution(false);
            });
        }

        private List<IGH_DocumentObject> FindTargets(GH_Document document, string targetName)
        {
            List<IGH_DocumentObject> controlTargets = FindTargetsFromControlOutput();
            if (controlTargets.Count > 0)
                return controlTargets;

            return FindTargetsByName(document, targetName);
        }

        private List<IGH_DocumentObject> FindTargetsFromControlOutput()
        {
            if (Params.Output.Count == 0)
                return new List<IGH_DocumentObject>();

            return Params.Output[0].Recipients
                .Select(item => item?.Attributes?.GetTopLevel?.DocObject)
                .Where(item => item != null)
                .Where(item => item.InstanceGuid != InstanceGuid)
                .Where(item => item is IGH_ActiveObject)
                .GroupBy(item => item.InstanceGuid)
                .Select(group => group.First())
                .ToList();
        }

        private List<IGH_DocumentObject> FindTargetsByName(GH_Document document, string targetName)
        {
            if (document == null || string.IsNullOrWhiteSpace(targetName))
                return new List<IGH_DocumentObject>();

            string name = targetName.Trim();
            return document.Objects
                .Where(item => item != null)
                .Where(item => item.InstanceGuid != InstanceGuid)
                .Where(item => item is IGH_ActiveObject)
                .Where(item => string.Equals(item.NickName, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static List<IGH_DocumentObject> FindTargetsById(GH_Document document, IEnumerable<Guid> ids)
        {
            if (document == null || ids == null)
                return new List<IGH_DocumentObject>();

            HashSet<Guid> idSet = new HashSet<Guid>(ids);
            return document.Objects
                .Where(item => item != null && idSet.Contains(item.InstanceGuid))
                .ToList();
        }

        private static void SetObjectsEnabled(IEnumerable<IGH_DocumentObject> targets, bool enabled, bool expire)
        {
            if (targets == null)
                return;

            foreach (IGH_DocumentObject target in targets)
                SetObjectEnabled(target, enabled, expire);
        }

        private static void SetObjectEnabled(IGH_DocumentObject target, bool enabled, bool expire)
        {
            if (target is IGH_ActiveObject activeObject)
            {
                activeObject.Locked = !enabled;
                if (expire)
                    activeObject.ExpireSolution(false);
            }
        }

        private void SubscribeDocument(GH_Document document)
        {
            if (_subscribedDocument == document)
                return;

            UnsubscribeDocument();
            _subscribedDocument = document;
            _subscribedDocument.SolutionEnd += OnSolutionEnd;
        }

        private void UnsubscribeDocument()
        {
            if (_subscribedDocument == null)
                return;

            _subscribedDocument.SolutionEnd -= OnSolutionEnd;
            _subscribedDocument = null;
        }

        private void SetStatusOutputs(IGH_DataAccess DA)
        {
            DA.SetData(1, string.Format(CultureInfo.InvariantCulture, "本次计算耗时{0:0.###}秒", _lastElapsedSeconds));
            DA.SetData(2, _targetIds.Count);
            DA.SetData(3, _message);
            DA.SetData(4, _donePulse);
        }

        protected override Bitmap Icon
        {
            get
            {
                Bitmap bitmap = new Bitmap(24, 24);
                bitmap.SetResolution(96, 96);

                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.Clear(Color.Transparent);

                    using (Pen wirePen = new Pen(Color.FromArgb(54, 54, 54), 2.2f))
                    using (Pen darkPen = new Pen(Color.FromArgb(42, 42, 42), 1.4f))
                    using (SolidBrush greenBrush = new SolidBrush(Color.FromArgb(116, 201, 70)))
                    using (SolidBrush blockBrush = new SolidBrush(Color.FromArgb(188, 194, 198)))
                    using (SolidBrush blueBrush = new SolidBrush(Color.FromArgb(64, 160, 220)))
                    using (SolidBrush orangeBrush = new SolidBrush(Color.FromArgb(238, 122, 34)))
                    using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                    {
                        graphics.DrawBezier(wirePen, 5.5f, 11.5f, 8.5f, 3.5f, 15.5f, 19.5f, 20.5f, 11.5f);

                        graphics.FillEllipse(greenBrush, 1.5f, 7.5f, 8f, 8f);
                        graphics.DrawEllipse(darkPen, 1.5f, 7.5f, 8f, 8f);

                        graphics.FillRoundedRectangle(blockBrush, new RectangleF(9f, 6.5f, 7.5f, 10f), 1.8f);
                        graphics.DrawRoundedRectangle(darkPen, new RectangleF(9f, 6.5f, 7.5f, 10f), 1.8f);

                        graphics.FillRectangle(orangeBrush, 10.5f, 17f, 4.5f, 2.5f);
                        graphics.DrawRectangle(darkPen, 10.5f, 17f, 4.5f, 2.5f);

                        graphics.FillEllipse(blueBrush, 17f, 7.5f, 6.5f, 6.5f);
                        graphics.DrawEllipse(darkPen, 17f, 7.5f, 6.5f, 6.5f);

                        PointF[] check =
                        {
                            new PointF(18.4f, 10.8f),
                            new PointF(20.0f, 12.4f),
                            new PointF(22.2f, 9.3f)
                        };
                        graphics.DrawLines(new Pen(Color.White, 1.3f), check);

                        PointF[] start =
                        {
                            new PointF(4.0f, 11.4f),
                            new PointF(5.2f, 12.7f),
                            new PointF(7.2f, 9.8f)
                        };
                        graphics.DrawLines(new Pen(Color.White, 1.2f), start);
                    }
                }

                return bitmap;
            }
        }

        public override Guid ComponentGuid => new Guid("4B75D00F-B62F-42CB-BBAE-F091C1B51BBD");
    }

    internal static class HoldEnableIconGraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, RectangleF bounds, float radius)
        {
            using (GraphicsPath path = CreateRoundedRectanglePath(bounds, radius))
                graphics.FillPath(brush, path);
        }

        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF bounds, float radius)
        {
            using (GraphicsPath path = CreateRoundedRectanglePath(bounds, radius))
                graphics.DrawPath(pen, path);
        }

        private static GraphicsPath CreateRoundedRectanglePath(RectangleF bounds, float radius)
        {
            float diameter = radius * 2f;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
