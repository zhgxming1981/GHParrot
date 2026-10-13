using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class DataInterceptor : GH_Component, IGH_VariableParameterComponent
    {
        private bool _lastForwardInput;
        private bool _forwardInputInitialized;
        private bool _inputDataInitialized;
        private bool _publishOutputs;
        private uint _lastRhinoDocumentSerialNumber;
        private readonly List<GH_Structure<IGH_Goo>> _forwardedData = new List<GH_Structure<IGH_Goo>>();
        private readonly List<bool> _hasForwardedData = new List<bool>();
        private readonly List<GH_Structure<IGH_Goo>> _lastInputData = new List<GH_Structure<IGH_Goo>>();
        private readonly List<bool> _hasLastInputData = new List<bool>();
        private readonly List<bool> _inputDataDirty = new List<bool>();
        internal bool ButtonForward { get; set; }
        internal bool ButtonClear { get; set; }
        internal bool HasForwardedData => _hasForwardedData.Exists(hasData => hasData);

        public DataInterceptor()
            : base("数据拦截", "拦截",
                "仅在转发信号触发时原样转发一次数据。",
                "Parrot", "Data")
        {
            _lastRhinoDocumentSerialNumber = RhinoDoc.ActiveDoc?.RuntimeSerialNumber ?? 0;
            RhinoDoc.ActiveDocumentChanged += OnActiveRhinoDocumentChanged;
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("数据1", "D1", "要转发的数据。", GH_ParamAccess.tree);
            pManager[0].Optional = true;
            pManager.AddBooleanParameter("转发", "F", "从 False 变为 True 时转发一次，可连接 Button。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("数据1", "D1", "触发时原样转发的数据。", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int dataPortCount = Params.Input.Count - 1;
            bool forwardInput = false;
            DA.GetData(dataPortCount, ref forwardInput);
            List<GH_Structure<IGH_Goo>> inputData;
            List<bool> hasInputData;
            List<bool> inputDataChanged;
            ReadAndRememberInputData(DA, dataPortCount, out inputData, out hasInputData, out inputDataChanged);

            if (ButtonClear)
            {
                ClearForwardedData();
                _publishOutputs = true;
                _lastForwardInput = forwardInput;
                ButtonClear = false;
                ButtonForward = false;
                MarkDirtyInputPorts(inputDataChanged);
                ShowUnforwardedInputWarnings();
                OutputForwardedData(DA);
                return;
            }

            bool inputTriggered = _forwardInputInitialized && forwardInput && !_lastForwardInput;
            _forwardInputInitialized = true;
            _lastForwardInput = forwardInput;
            bool shouldForward = ButtonForward || inputTriggered;
            ButtonForward = false;

            MarkDirtyInputPorts(inputDataChanged);
            if (shouldForward)
                ClearDirtyInputPorts();

            ShowUnforwardedInputWarnings();

            if (!shouldForward)
            {
                OutputForwardedData(DA);
                return;
            }

            EnsureForwardCacheSize(dataPortCount);
            for (int i = 0; i < dataPortCount; i++)
            {
                if (hasInputData[i])
                {
                    // 独立保存树容器，树内的数据对象仍保持原引用。
                    _forwardedData[i] = inputData[i].ShallowDuplicate();
                    _hasForwardedData[i] = true;
                }
                else
                {
                    _forwardedData[i] = null;
                    _hasForwardedData[i] = false;
                }
            }

            OutputForwardedData(DA);
            _publishOutputs = true;
        }

        protected override void ExpireDownStreamObjects()
        {
            // 上游失效只能使本电池重新收集输入，不能沿输出连线传播。
            // 明确转发或清零后，在输出写入完成时单独通知下游。
        }

        protected override void AfterSolveInstance()
        {
            base.AfterSolveInstance();
            if (!_publishOutputs)
                return;

            _publishOutputs = false;
            GH_Document document = OnPingDocument();
            if (document == null)
                return;

            // 正延迟保证回调在当前整轮求解结束后执行。
            // 只使下游失效，不重新请求本电池转发。
            document.ScheduleSolution(1, scheduledDocument =>
            {
                if (!ReferenceEquals(OnPingDocument(), scheduledDocument))
                    return;

                var recipients = new HashSet<IGH_Param>();
                foreach (IGH_Param output in Params.Output)
                {
                    foreach (IGH_Param recipient in output.Recipients)
                        recipients.Add(recipient);
                }

                foreach (IGH_Param recipient in recipients)
                    recipient.ExpireSolution(false);
            });
        }

        private void OutputForwardedData(IGH_DataAccess DA)
        {
            int dataPortCount = Params.Input.Count - 1;
            EnsureForwardCacheSize(dataPortCount);
            for (int i = 0; i < dataPortCount; i++)
            {
                if (_hasForwardedData[i])
                    DA.SetDataTree(i, _forwardedData[i]);
            }

        }

        private void ReadAndRememberInputData(IGH_DataAccess DA, int dataPortCount,
            out List<GH_Structure<IGH_Goo>> inputData, out List<bool> hasInputData,
            out List<bool> inputDataChanged)
        {
            inputData = new List<GH_Structure<IGH_Goo>>(dataPortCount);
            hasInputData = new List<bool>(dataPortCount);
            inputDataChanged = new List<bool>(dataPortCount);

            for (int i = 0; i < dataPortCount; i++)
            {
                GH_Structure<IGH_Goo> data;
                bool hasData = DA.GetDataTree(i, out data);
                inputData.Add(data);
                hasInputData.Add(hasData);

                bool changed = _inputDataInitialized &&
                    (i >= _lastInputData.Count || hasData != _hasLastInputData[i] ||
                     (hasData && !DataTreesEqual(data, _lastInputData[i])));
                inputDataChanged.Add(changed);
            }

            _lastInputData.Clear();
            _lastInputData.AddRange(inputData);
            _hasLastInputData.Clear();
            _hasLastInputData.AddRange(hasInputData);
            _inputDataInitialized = true;
        }

        private void MarkDirtyInputPorts(List<bool> inputDataChanged)
        {
            EnsureDirtyInputPortSize(inputDataChanged.Count);
            for (int i = 0; i < inputDataChanged.Count; i++)
            {
                if (inputDataChanged[i])
                    _inputDataDirty[i] = true;
            }
        }

        private void ClearDirtyInputPorts()
        {
            for (int i = 0; i < _inputDataDirty.Count; i++)
                _inputDataDirty[i] = false;
        }

        private void ShowUnforwardedInputWarnings()
        {
            for (int i = 0; i < _inputDataDirty.Count; i++)
            {
                if (_inputDataDirty[i])
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"第{i + 1}端口输入数据已经更新，但未转发。");
            }
        }

        private void EnsureDirtyInputPortSize(int dataPortCount)
        {
            while (_inputDataDirty.Count < dataPortCount)
                _inputDataDirty.Add(false);

            while (_inputDataDirty.Count > dataPortCount)
                _inputDataDirty.RemoveAt(_inputDataDirty.Count - 1);
        }

        private static bool DataTreesEqual(GH_Structure<IGH_Goo> first, GH_Structure<IGH_Goo> second)
        {
            if (ReferenceEquals(first, second))
                return true;
            if (first == null || second == null || first.PathCount != second.PathCount)
                return false;

            for (int i = 0; i < first.PathCount; i++)
            {
                if (!first.Paths[i].Equals(second.Paths[i]))
                    return false;

                List<IGH_Goo> firstBranch = first.Branches[i];
                List<IGH_Goo> secondBranch = second.Branches[i];
                if (firstBranch.Count != secondBranch.Count)
                    return false;

                for (int j = 0; j < firstBranch.Count; j++)
                {
                    if (!object.Equals(firstBranch[j], secondBranch[j]))
                        return false;
                }
            }

            return true;
        }

        private void OnActiveRhinoDocumentChanged(object sender, EventArgs e)
        {
            uint currentSerialNumber = RhinoDoc.ActiveDoc?.RuntimeSerialNumber ?? 0;
            if (currentSerialNumber == _lastRhinoDocumentSerialNumber)
                return;

            _lastRhinoDocumentSerialNumber = currentSerialNumber;
            ClearForwardedData();
            _publishOutputs = true;
            ButtonForward = false;
            ButtonClear = false;
            _forwardInputInitialized = false;
            _lastForwardInput = false;
            _inputDataInitialized = false;
            _lastInputData.Clear();
            _hasLastInputData.Clear();
            _inputDataDirty.Clear();

            GH_Document document = OnPingDocument();
            document?.ScheduleSolution(1, doc => ExpireSolution(false));
        }

        private void EnsureForwardCacheSize(int dataPortCount)
        {
            while (_forwardedData.Count < dataPortCount)
            {
                _forwardedData.Add(null);
                _hasForwardedData.Add(false);
            }

            while (_forwardedData.Count > dataPortCount)
            {
                _forwardedData.RemoveAt(_forwardedData.Count - 1);
                _hasForwardedData.RemoveAt(_hasForwardedData.Count - 1);
            }
        }

        private void ClearForwardedData()
        {
            for (int i = 0; i < _forwardedData.Count; i++)
            {
                _forwardedData[i] = null;
                _hasForwardedData[i] = false;
            }
        }

        public bool CanInsertParameter(GH_ParameterSide side, int index)
        {
            return side == GH_ParameterSide.Input && index < Params.Input.Count;
        }

        public bool CanRemoveParameter(GH_ParameterSide side, int index)
        {
            return side == GH_ParameterSide.Input && index < Params.Input.Count - 1 && Params.Input.Count > 2;
        }

        public IGH_Param CreateParameter(GH_ParameterSide side, int index)
        {
            return CreateDataParameter();
        }

        public bool DestroyParameter(GH_ParameterSide side, int index)
        {
            return CanRemoveParameter(side, index);
        }

        public void VariableParameterMaintenance()
        {
            int dataPortCount = Params.Input.Count - 1;
            while (Params.Output.Count < dataPortCount)
                Params.RegisterOutputParam(CreateDataParameter());

            while (Params.Output.Count > dataPortCount)
                Params.UnregisterOutputParameter(Params.Output[dataPortCount], true);

            for (int i = 0; i < dataPortCount; i++)
            {
                ConfigureDataParameter(Params.Input[i], i + 1, "要转发的数据。");
                ConfigureDataParameter(Params.Output[i], i + 1, "触发时原样转发的数据。");
            }

            IGH_Param forwardParam = Params.Input[Params.Input.Count - 1];
            forwardParam.Name = "转发";
            forwardParam.NickName = "F";
            forwardParam.Description = "从 False 变为 True 时转发一次，可连接 Button。";
            forwardParam.Access = GH_ParamAccess.item;
            forwardParam.Optional = true;
        }

        private static Param_GenericObject CreateDataParameter()
        {
            Param_GenericObject param = new Param_GenericObject();
            param.Access = GH_ParamAccess.tree;
            param.Optional = true;
            return param;
        }

        private static void ConfigureDataParameter(IGH_Param param, int number, string description)
        {
            param.Name = "数据" + number;
            param.NickName = "D" + number;
            param.Description = description;
            param.Access = GH_ParamAccess.tree;
            param.Optional = true;
        }

        public override void CreateAttributes()
        {
            Attributes = new DataInterceptorAttributes(this);
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            RhinoDoc.ActiveDocumentChanged -= OnActiveRhinoDocumentChanged;
            base.RemovedFromDocument(document);
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("A64C9C21-5BFE-4BCD-8D7D-6E2C7759F6D4");
    }

    internal sealed class DataInterceptorAttributes : GH_ComponentAttributes
    {
        public DataInterceptorAttributes(DataInterceptor owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 40.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF forwardButtonRect = new RectangleF(Bounds.X, Bounds.Bottom - 40.0f, Bounds.Width, 20.0f);
            forwardButtonRect.Inflate(-5.0f, -2.0f);
            RectangleF clearButtonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20.0f, Bounds.Width, 20.0f);
            clearButtonRect.Inflate(-5.0f, -2.0f);

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(forwardButtonRect, GH_Palette.Black))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            GH_Palette clearPalette = ((DataInterceptor)Owner).HasForwardedData
                ? GH_Palette.Black
                : GH_Palette.Grey;
            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(clearButtonRect, clearPalette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("转发", font, Brushes.White, forwardButtonRect, format);
                graphics.DrawString("清零", font, Brushes.White, clearButtonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button != MouseButtons.Left)
                return base.RespondToMouseDown(sender, e);

            RectangleF forwardButtonRect = new RectangleF(Bounds.X, Bounds.Bottom - 40.0f, Bounds.Width, 20.0f);
            forwardButtonRect.Inflate(-5.0f, -2.0f);
            RectangleF clearButtonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20.0f, Bounds.Width, 20.0f);
            clearButtonRect.Inflate(-5.0f, -2.0f);
            DataInterceptor owner = (DataInterceptor)Owner;
            if (forwardButtonRect.Contains(e.CanvasLocation))
                owner.ButtonForward = true;
            else if (clearButtonRect.Contains(e.CanvasLocation))
                owner.ButtonClear = true;
            else
                return base.RespondToMouseDown(sender, e);

            owner.ExpireSolution(true);
            return GH_ObjectResponse.Handled;
        }
    }
}
