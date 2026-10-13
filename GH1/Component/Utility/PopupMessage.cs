using System;
using System.Windows.Forms;
using Grasshopper.Kernel;

namespace NS_Parrot
{
    /// <summary>
    /// 在触发信号由 False 变为 True 时显示消息对话框。
    /// </summary>
    public class PopupMessage : GH_Component
    {
        private bool _lastTrigger;

        public PopupMessage()
            : base("弹框", "弹框",
                "触发输入由 False 变为 True 时，显示指定内容的弹框。",
                "Parrot", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("触发", "T", "True 时弹出对话框。", GH_ParamAccess.item, false);
            pManager.AddTextParameter("弹框内容", "内容", "对话框中显示的文字。", GH_ParamAccess.item, "提示");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool trigger = false;
            string message = "提示";
            DA.GetData(0, ref trigger);
            DA.GetData(1, ref message);

            if (trigger && !_lastTrigger)
                MessageBox.Show(message ?? string.Empty, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);

            _lastTrigger = trigger;
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return null; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("B94BBAF7-905F-4D5F-8B77-BB929A88B3B3"); }
        }
    }
}
