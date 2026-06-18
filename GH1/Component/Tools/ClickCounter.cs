using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ClickCounter : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        private bool _pulse;
        private Timer _pulseResetTimer;

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        public int Count { get; private set; }

        public ClickCounter()
          : base("ClickCounter", "点击计数",
              "点击组件按钮累计次数，输出初始值加点击次数，并在结果更新后输出一次脉冲",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("初始值", "初始值", "输出结果的初始值", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("结果", "结果", "初始值加点击次数", GH_ParamAccess.item);
            pManager.AddIntegerParameter("点击次数", "点击次数", "按钮累计点击次数", GH_ParamAccess.item);
            pManager.AddBooleanParameter("脉冲", "脉冲", "点击后在结果更新的同一次求解中输出一次 True，随后自动复位", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            int initialValue = 0;
            DA.GetData(0, ref initialValue);

            DA.SetData(0, initialValue + Count);
            DA.SetData(1, Count);
            DA.SetData(2, _pulse);

            if (_pulse)
                SchedulePulseReset();
        }

        public override void CreateAttributes()
        {
            Attributes = new ClickCounterAttributes(this);
        }

        public void Increment()
        {
            Count++;
            _pulse = true;
            CurrentButtonColor = ButtonColor.Black;
            ExpireSolution(true);
        }

        private void SchedulePulseReset()
        {
            if (_pulseResetTimer != null)
                return;

            _pulseResetTimer = new Timer();
            _pulseResetTimer.Interval = 20;
            _pulseResetTimer.Tick += (sender, args) =>
            {
                StopPulseResetTimer();
                _pulse = false;
                ExpireSolution(true);
            };
            _pulseResetTimer.Start();
        }

        private void StopPulseResetTimer()
        {
            if (_pulseResetTimer == null)
                return;

            _pulseResetTimer.Stop();
            _pulseResetTimer.Dispose();
            _pulseResetTimer = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            StopPulseResetTimer();
            base.RemovedFromDocument(document);
        }

        protected override Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("0D7D22C5-0565-4C39-9D94-8D9B0CF05C52"); }
        }
    }

    internal class ClickCounterAttributes : GH_ComponentAttributes
    {
        private const float ButtonHeight = 20.0f;

        public ClickCounterAttributes(ClickCounter component) : base(component)
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

            RectangleF buttonRect = GetButtonRect();
            if (channel == GH_CanvasChannel.Objects)
            {
                ClickCounter owner = (ClickCounter)Owner;
                GH_Palette palette = owner.CurrentButtonColor == ClickCounter.ButtonColor.Black
                    ? GH_Palette.Black
                    : GH_Palette.Grey;

                using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                {
                    capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                }

                using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
                using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    graphics.DrawString("点击", font, Brushes.White, buttonRect, format);
                }
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (e.Button == MouseButtons.Left && GetButtonRect().Contains(e.CanvasLocation))
            {
                ClickCounter owner = (ClickCounter)Owner;
                owner.CurrentButtonColor = ClickCounter.ButtonColor.Grey;
                owner.Increment();
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
