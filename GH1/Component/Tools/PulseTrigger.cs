using CommonFunction;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class PulseTrigger : GH_Component, IGH_VariableParameterComponent
    {
        private const int PulseInterval = 200;

        private bool _pulse;
        private bool _resettingPulse;
        private Timer _resetTimer;

        public PulseTrigger()
          : base("PulseTrigger", "脉冲触发",
              "所有可变输入都有数据时输出一次True脉冲，用于串联需要Run触发的电池",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("数据1", "D1", "上游完成信号或数据", GH_ParamAccess.list);
            pManager[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("运行", "Run", "一次True脉冲，可连接到下游电池Run输入", GH_ParamAccess.item);
            pManager.AddTextParameter("状态", "状态", "当前触发状态", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            bool inputsComplete = AllInputsHaveData();
            if (_resettingPulse)
            {
                _resettingPulse = false;
                _pulse = false;
            }
            else if (inputsComplete)
                StartPulse();
            else
                StopPulse();

            string status;
            if (!inputsComplete)
                status = "等待所有输入都有数据。";
            else if (_pulse)
                status = "已触发脉冲。";
            else
                status = "输入完整，等待上游数据变化。";

            DA.SetData(0, _pulse);
            DA.SetData(1, status);
        }

        private bool AllInputsHaveData()
        {
            if (Params.Input.Count == 0)
                return false;

            for (int i = 0; i < Params.Input.Count; i++)
            {
                IGH_Param param = Params.Input[i];
                if (param == null || param.VolatileDataCount == 0)
                    return false;
            }

            return true;
        }

        private void StartPulse()
        {
            _pulse = true;
            _resettingPulse = false;
            _resetTimer?.Stop();
            _resetTimer?.Dispose();

            _resetTimer = new Timer();
            _resetTimer.Interval = PulseInterval;
            _resetTimer.Tick += (sender, args) =>
            {
                _resetTimer.Stop();
                _resetTimer.Dispose();
                _resetTimer = null;
                _resettingPulse = true;
                ExpireSolution(true);
            };
            _resetTimer.Start();
        }

        private void StopPulse()
        {
            _pulse = false;
            _resettingPulse = false;
            _resetTimer?.Stop();
            _resetTimer?.Dispose();
            _resetTimer = null;
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            return side == GH_ParameterSide.Input;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            return side == GH_ParameterSide.Input && Params.Input.Count > 1;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            Param_GenericObject param = new Param_GenericObject();
            param.Name = "数据" + (index + 1);
            param.NickName = "D" + (index + 1);
            param.Description = "上游完成信号或数据";
            param.Access = GH_ParamAccess.list;
            param.Optional = true;
            return param;
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            return side == GH_ParameterSide.Input && Params.Input.Count > 1;
        }

        public void VariableParameterMaintenance()
        {
            for (int i = 0; i < Params.Input.Count; i++)
            {
                IGH_Param param = Params.Input[i];
                param.Name = "数据" + (i + 1);
                param.NickName = "D" + (i + 1);
                param.Description = "上游完成信号或数据";
                param.Access = GH_ParamAccess.list;
                param.Optional = true;
            }
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_PulseTrigger"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("9B2DF5EF-E409-4F91-AF88-5C8C90C25578"); }
        }
    }
}
