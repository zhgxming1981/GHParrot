using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ManualAutoForwarder : GH_Component
    {
        private readonly List<IGH_Goo> _cachedData = new List<IGH_Goo>();
        private Timer _autoTimer;
        private Timer _doneResetTimer;
        private bool _lastRun;
        private bool _hasCurrent;
        private bool _running;
        private bool _donePulse;
        private int _currentIndex = -1;
        private int _nextIndex;
        private string _status = "等待触发";

        public ManualAutoForwarder()
          : base("手动自动转发器", "转发器",
              "按 Run 手动逐项转发列表数据；Seconds 有值时自动按间隔逐项转发。",
              "Parrot", "Data")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "数据", "需要依次转发给下游的数据列表。", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Run", "Run", "False 变 True 时触发。Seconds 为空时手动转发一个；Seconds 有值时启动自动转发。", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Seconds", "Seconds", "每个数据保持的秒数。为空时为手动模式。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("循环", "Loop", "到末尾后是否回到第一个继续。", GH_ParamAccess.item, false);

            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("数据", "数据", "当前转发的数据。", GH_ParamAccess.item);
            pManager.AddIntegerParameter("索引", "索引", "当前转发数据在列表中的索引。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("运行", "运行", "自动模式正在运行时为 True。", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Done", "Done", "每次转发新数据时输出一个 True 脉冲。", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "状态", "当前转发状态。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<IGH_Goo> inputData = new List<IGH_Goo>();
            bool run = false;
            bool loop = false;
            double seconds = 0.0;
            bool hasSeconds = false;

            DA.GetDataList(0, inputData);
            DA.GetData(1, ref run);
            hasSeconds = DA.GetData(2, ref seconds) && seconds > 0.0;
            DA.GetData(3, ref loop);

            bool risingEdge = run && !_lastRun;
            _lastRun = run;

            UpdateCache(inputData);

            if (_cachedData.Count == 0)
            {
                StopAutoTimer();
                _running = false;
                _hasCurrent = false;
                _currentIndex = -1;
                _nextIndex = 0;
                _status = "没有可转发的数据";
            }
            else if (risingEdge)
            {
                if (hasSeconds)
                    StartAuto(seconds, loop);
                else
                    ForwardNext(loop, false);
            }
            else if (_running && !hasSeconds)
            {
                StopAutoTimer();
                _running = false;
                _status = _hasCurrent ? "Seconds 为空，已停止自动转发" : "等待触发";
            }

            SetOutputs(DA);

            if (_donePulse)
                ScheduleDoneReset();
        }

        private void UpdateCache(List<IGH_Goo> inputData)
        {
            _cachedData.Clear();
            if (inputData == null)
                return;

            foreach (IGH_Goo item in inputData)
                _cachedData.Add(item?.Duplicate());

            if (_cachedData.Count == 0)
                return;

            if (_nextIndex < 0 || _nextIndex >= _cachedData.Count)
                _nextIndex = 0;

            if (_currentIndex >= _cachedData.Count)
            {
                _currentIndex = -1;
                _hasCurrent = false;
            }
        }

        private void StartAuto(double seconds, bool loop)
        {
            ForwardNext(loop, true);

            if (_running)
                ScheduleAutoTimer(seconds, loop);
        }

        private void ForwardNext(bool loop, bool autoMode)
        {
            if (_cachedData.Count == 0)
            {
                _status = "没有可转发的数据";
                _running = false;
                StopAutoTimer();
                return;
            }

            if (_nextIndex >= _cachedData.Count)
            {
                if (!loop)
                {
                    _running = false;
                    StopAutoTimer();
                    _status = "已到列表末尾";
                    return;
                }

                _nextIndex = 0;
            }

            _currentIndex = _nextIndex;
            _hasCurrent = true;
            _donePulse = true;
            _nextIndex++;
            _running = autoMode && (loop || _nextIndex < _cachedData.Count);
            _status = autoMode
                ? "自动转发第 " + (_currentIndex + 1) + " 个数据"
                : "手动转发第 " + (_currentIndex + 1) + " 个数据";
        }

        private void ScheduleAutoTimer(double seconds, bool loop)
        {
            StopAutoTimer();

            int interval = Math.Max(1, (int)Math.Round(seconds * 1000.0));
            _autoTimer = new Timer();
            _autoTimer.Interval = interval;
            _autoTimer.Tick += (sender, args) =>
            {
                StopAutoTimer();
                ForwardNext(loop, true);
                ExpireSolution(true);

                if (_running)
                    ScheduleAutoTimer(seconds, loop);
            };
            _autoTimer.Start();
        }

        private void ScheduleDoneReset()
        {
            if (_doneResetTimer != null)
                return;

            _doneResetTimer = new Timer();
            _doneResetTimer.Interval = 50;
            _doneResetTimer.Tick += (sender, args) =>
            {
                StopDoneResetTimer();
                _donePulse = false;
                ExpireSolution(true);
            };
            _doneResetTimer.Start();
        }

        private void SetOutputs(IGH_DataAccess DA)
        {
            if (_hasCurrent && _currentIndex >= 0 && _currentIndex < _cachedData.Count)
            {
                DA.SetData(0, _cachedData[_currentIndex]?.Duplicate());
                DA.SetData(1, _currentIndex);
            }
            else
            {
                DA.SetData(1, -1);
            }

            DA.SetData(2, _running);
            DA.SetData(3, _donePulse);
            DA.SetData(4, _status);
        }

        private void StopAutoTimer()
        {
            if (_autoTimer == null)
                return;

            _autoTimer.Stop();
            _autoTimer.Dispose();
            _autoTimer = null;
        }

        private void StopDoneResetTimer()
        {
            if (_doneResetTimer == null)
                return;

            _doneResetTimer.Stop();
            _doneResetTimer.Dispose();
            _doneResetTimer = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopAutoTimer();
            StopDoneResetTimer();
            base.RemovedFromDocument(document);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_PulseTrigger"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("44C6B9A4-9B82-40B7-9FC3-4310C2E1D011"); }
        }
    }
}
