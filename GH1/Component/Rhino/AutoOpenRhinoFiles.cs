using Grasshopper.Kernel;
using Rhino;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel.Attributes;
using System.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class AutoOpenRhinoFiles : GH_Component
    {
        private List<string> _files = new List<string>();
        private int _nextIndex;
        private int _startIndex;
        private int _currentIndex = -1;
        private string _currentFileName;
        private bool _lastNext, _queued, _opening, _pulse, _resetScheduled;
        private uint _documentSerial;
        private string _error;
        private int _generation;
        private bool _paused, _startRequested;
        private Timer _openTimer;

        public AutoOpenRhinoFiles() : base("自动打开Rhino文件", "自动打开",
            "通过按钮或下一个输入按列表打开3dm文件，每次触发打开一份。打开成功输出一次True。当前文档有未保存修改时停止。",
            "Parrot", "Rhino") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("文件列表", "文件", "按顺序打开的3dm文件完整路径，请输入单个列表。", GH_ParamAccess.list);
            p.AddBooleanParameter("打开下一个", "下一个", "False变为True时打开下一份；打开及脉冲复位期间忽略重复触发。", GH_ParamAccess.item, false);
            p.AddIntegerParameter("起始索引", "起始索引", "从指定索引开始打开，从0开始；修改后重置进度，启动时也从此索引开始。", GH_ParamAccess.item, 0);
            p[0].Optional = true;
            p[1].Optional = true;
            p[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddBooleanParameter("打开完成", "打开完成", "文件打开成功后输出一次True，随后自动恢复False。", GH_ParamAccess.item);
            p.AddIntegerParameter("当前序号", "序号", "最近一次成功打开的文件在列表中的序号，从0开始；尚未打开时为空。", GH_ParamAccess.item);
            p.AddTextParameter("文件名称", "名称", "最近一次成功打开的文件名称，不带扩展名；尚未打开时为空。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.SetData(0, false);
            if (DA.Iteration != 0)
            {
                Stop("文件和触发输入必须为单个列表，请先合并数据分支。");
                return;
            }
            var files = new List<string>();
            bool next = false;
            int startIndex = 0;
            DA.GetDataList(0, files);
            DA.GetData(1, ref next);
            DA.GetData(2, ref startIndex);
            if (_startIndex != startIndex || !_files.SequenceEqual(files, StringComparer.OrdinalIgnoreCase))
            {
                _startIndex = startIndex;
                Reset();
                _files = files;
            }
            bool rising = next && !_lastNext;
            _lastNext = next;
            if (_startRequested)
            {
                _startRequested = false;
                Reset();
                _queued = true;
            }
            if (_startIndex < 0 || (_files.Count > 0 && _startIndex >= _files.Count))
            {
                Stop("起始索引超出范围：必须大于或等于0，且小于文件列表数量。");
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _error);
                Message = "已停止";
                return;
            }
            if (_pulse && RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _documentSerial)
                Stop("当前Rhino文档已被切换，批处理停止。");
            if (rising && !_paused && !_opening && !_pulse && !_resetScheduled)
            {
                _error = null;
                _queued = true;
            }
            DA.SetData(0, _pulse);
            if (_currentIndex >= 0)
            {
                DA.SetData(1, _currentIndex);
                DA.SetData(2, _currentFileName);
            }
            if (_error != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _error);
            Message = _error != null ? "已停止" : _paused ? "已暂停 " + _nextIndex + "/" + _files.Count : _opening ? "正在打开" :
                _files.Count > 0 && _nextIndex >= _files.Count ? "列表完成" : _nextIndex + "/" + _files.Count;
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            ScheduleOpen();
            if (!_pulse || _resetScheduled) return;
            var document = OnPingDocument();
            if (document == null) return;
            _resetScheduled = true;
            int generation = _generation;
            document.ScheduleSolution(1, doc =>
            {
                if (OnPingDocument() != doc || generation != _generation) return;
                _pulse = false;
                _resetScheduled = false;
                ExpireSolution(false);
            });
        }

        private void ScheduleOpen()
        {
            if (!_queued || _paused || _openTimer != null || OnPingDocument() == null) return;
            _openTimer = new Timer { Interval = 50 };
            _openTimer.Tick += OnOpenTimer;
            _openTimer.Start();
        }

        private void CancelOpenTimer()
        {
            if (_openTimer == null) return;
            _openTimer.Stop();
            _openTimer.Tick -= OnOpenTimer;
            _openTimer.Dispose();
            _openTimer = null;
        }

        private void OnOpenTimer(object sender, EventArgs e)
        {
            CancelOpenTimer();
            try { OpenQueuedFile(); }
            finally { ScheduleOpen(); }
        }

        private void ShowStatus(string status)
        {
            if (Message == status) return;
            Message = status;
            Grasshopper.Instances.ActiveCanvas?.Invalidate();
        }

        private void OpenQueuedFile()
        {
            var document = OnPingDocument();
            if (!_queued || _paused || document == null) return;
            if (_opening || _pulse || _resetScheduled) { ShowStatus("等待本轮完成及脉冲复位"); return; }
            if (Locked) { ShowStatus("等待解除组件锁定"); return; }
            if (!document.Enabled || !GH_Document.EnableSolutions) { ShowStatus("等待启用GH求解器"); return; }
            if (document.SolutionDepth != 0) { ShowStatus("等待当前求解结束"); return; }
            if (Grasshopper.Instances.ActiveCanvas?.Document != document) { ShowStatus("等待切回此GH画布"); return; }
            if (RhinoDoc.ActiveDoc?.InCommand(true) > 0) { ShowStatus("等待Rhino命令结束"); return; }
            if (document.Objects.OfType<AutoOpenRhinoFiles>().Any(c => c != this && (c._opening || c._pulse)))
            { ShowStatus("等待其他自动打开任务完成"); return; }

            // 与手动导出模型一致，通过UI定时器在求解结束后执行Rhino操作。
            _queued = false;
            if (_files.Count == 0)
            {
                Stop("未执行：文件列表为空。");
                Refresh();
                return;
            }
            if (_nextIndex >= _files.Count)
            {
                Refresh();
                return;
            }
            _opening = true;
            try
            {
                string path = Path.GetFullPath((_files[_nextIndex] ?? string.Empty).Trim().Trim('"'));
                if (!string.Equals(Path.GetExtension(path), ".3dm", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    throw new IOException("文件不存在或不是3dm文件: " + path);
                if (RhinoDoc.ActiveDoc?.Modified == true)
                    throw new InvalidOperationException("当前Rhino文档有未保存修改。请先处理修改，再触发下一个。");

                var opened = RhinoDoc.Open(path, out bool alreadyOpen);
                if (opened == null || RhinoDoc.ActiveDoc?.RuntimeSerialNumber != opened.RuntimeSerialNumber ||
                    !string.Equals(opened.Path, path, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Rhino未能打开指定文件: " + path);

                _currentIndex = _nextIndex;
                _currentFileName = Path.GetFileNameWithoutExtension(path);
                _nextIndex++;
                _documentSerial = opened.RuntimeSerialNumber;
                _pulse = true;
                _error = null;
            }
            catch (Exception ex) { Stop("打开停止: " + ex.Message); }
            finally { _opening = false; }
            Refresh();
        }

        private void Refresh()
        {
            var document = OnPingDocument();
            document?.ScheduleSolution(1, doc => { if (OnPingDocument() == doc) ExpireSolution(false); });
        }

        private void Stop(string error)
        {
            CancelOpenTimer();
            _queued = _pulse = false;
            _error = error;
        }

        private void Reset()
        {
            CancelOpenTimer();
            _generation++;
            _nextIndex = _startIndex;
            _currentIndex = -1;
            _currentFileName = null;
            _paused = false;
            _queued = _pulse = _resetScheduled = false;
            _error = null;
        }

        internal bool CanStart => !Locked && !_opening && !_pulse && !_resetScheduled;
        internal bool CanContinue => !Locked && _paused && _error == null;

        internal void StartFromButton()
        {
            if (!CanStart)
            {
                ShowStatus(Locked ? "组件已锁定" : "当前操作尚未完成");
                return;
            }
            _startRequested = true;
            ShowStatus("准备从起始索引开始");
            ExpireSolution(true);
        }

        internal void PauseFromButton()
        {
            if (Locked) return;
            _startRequested = false;
            _paused = true;
            CancelOpenTimer();
            ShowStatus("已暂停");
            // 保留进度，只阻止打开下一份。
            Refresh();
        }

        internal void ContinueFromButton()
        {
            if (!CanContinue)
            {
                ShowStatus(_error != null ? "请处理错误后重新启动" : "当前未暂停");
                return;
            }
            _paused = false;
            if (!_opening && _nextIndex < _files.Count)
                _queued = true;
            ScheduleOpen();
            Refresh();
        }

        public override void CreateAttributes()
        {
            Attributes = new AutoOpenRhinoFilesAttributes(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendItem(menu, "停止并重置到起始索引", (sender, args) => { Reset(); ExpireSolution(true); });
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            _startRequested = false;
            Reset();
            base.RemovedFromDocument(document);
        }

        protected override System.Drawing.Bitmap Icon => GeneratedIcon.Get("gen_CurrentRhinoDocument");
        public override Guid ComponentGuid => new Guid("52991403-1687-4D14-BF9F-E9C56A40DDAC");
    }

    internal sealed class AutoOpenRhinoFilesAttributes : GH_ComponentAttributes
    {
        private const float RowHeight = 22f;
        private static readonly string[] Labels = { "启动", "停止", "继续" };

        public AutoOpenRhinoFilesAttributes(AutoOpenRhinoFiles owner) : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + RowHeight * 3);
        }

        private RectangleF ButtonBounds(int index)
        {
            return new RectangleF(Bounds.X + 4, Bounds.Bottom - RowHeight * (3 - index) + 2, Bounds.Width - 8, RowHeight - 4);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < Labels.Length; i++)
                {
                    RectangleF button = ButtonBounds(i);
                    using (var capsule = GH_Capsule.CreateCapsule(button, GH_Palette.Black))
                        capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                    graphics.DrawString(Labels[i], GH_FontServer.Small, IsEnabled(i) ? Brushes.White : Brushes.Gray, button, format);
                }
            }
        }

        private bool IsEnabled(int index)
        {
            var component = (AutoOpenRhinoFiles)Owner;
            return index == 0 ? component.CanStart : index == 2 ? component.CanContinue : !Owner.Locked;
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < Labels.Length; i++)
                {
                    if (!ButtonBounds(i).Contains(e.CanvasLocation)) continue;
                    if (!Owner.Locked)
                    {
                        var component = (AutoOpenRhinoFiles)Owner;
                        if (i == 0) component.StartFromButton();
                        else if (i == 1) component.PauseFromButton();
                        else component.ContinueFromButton();
                        sender.Invalidate();
                    }
                    return GH_ObjectResponse.Handled;
                }
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}
