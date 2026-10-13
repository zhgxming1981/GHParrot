using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class SetUserStringList : GH_Component
    {
        public enum ButtonColor { Black, Grey }

        public ButtonColor CurrentButtonColor { get; set; } = ButtonColor.Black;
        internal bool ButtonRun { get; set; }

        private bool _lastInputRun;

        public SetUserStringList()
          : base("批量写入UserString", "批量写入UserString",
              "给多个Rhino对象写入UserString；一个Value会写入全部对象，多个Value则按顺序一一对应。",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Guids", "Guids", "Rhino对象Guid列表", GH_ParamAccess.list);
            pManager.AddTextParameter("key", "key", "UserString的key", GH_ParamAccess.item);
            pManager.AddTextParameter("Values", "Values", "一个Value写入全部对象；多个Value与Guids按顺序对应", GH_ParamAccess.list);
            pManager.AddBooleanParameter("run", "run", "由False变为True时批量写入UserString", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Result", "Result", "每个Guid的写入结果", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<object> rawGuids = new List<object>();
            List<string> values = new List<string>();
            string key = string.Empty;
            bool inputRun = false;

            DA.GetData(3, ref inputRun);
            bool shouldRun = ButtonRun || (inputRun && !_lastInputRun);
            _lastInputRun = inputRun;
            ButtonRun = false;

            if (!shouldRun)
                return;

            List<string> results = new List<string>();
            if (!DA.GetDataList(0, rawGuids) || rawGuids.Count == 0)
            {
                ReportInputError(DA, results, "未读取到Guid列表。请确认Guids输入已连接且包含有效Guid。");
                return;
            }
            if (!DA.GetData(1, ref key))
            {
                ReportInputError(DA, results, "未读取到key。");
                return;
            }
            if (!DA.GetDataList(2, values) || values.Count == 0)
            {
                ReportInputError(DA, results, "未读取到Value列表。请确认Values输入已连接。");
                return;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                ReportInputError(DA, results, "key不能为空。");
                return;
            }

            if (values.Count != 1 && values.Count != rawGuids.Count)
            {
                ReportInputError(DA, results,
                    $"Value数量必须为1或与Guid数量相同。当前Guid数量：{rawGuids.Count}，Value数量：{values.Count}。");
                return;
            }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null)
            {
                ReportInputError(DA, results, "当前没有可用的Rhino文档。");
                return;
            }

            int successCount = 0;

            for (int i = 0; i < rawGuids.Count; i++)
            {
                if (!TryGetGuid(rawGuids[i], out Guid id))
                {
                    results.Add($"[{i}] 无法转换为Guid：{rawGuids[i] ?? "<null>"}，未写入。");
                    continue;
                }

                RhinoObject obj = doc.Objects.FindId(id);
                if (obj == null)
                {
                    results.Add($"[{i}] {id}：找不到Rhino对象，未写入。");
                    continue;
                }

                string value = values.Count == 1 ? values[0] : values[i];
                ObjectAttributes attributes = obj.Attributes.Duplicate();
                attributes.SetUserString(key, value ?? string.Empty);

                if (!doc.Objects.ModifyAttributes(obj, attributes, true))
                {
                    string objectState = obj.IsHidden ? "对象已隐藏" : obj.IsLocked ? "对象已锁定" : "对象状态正常";
                    results.Add($"[{i}] {id}：写入失败（{objectState}）。");
                    continue;
                }

                RhinoObject updatedObject = doc.Objects.FindId(id);
                string actualValue = updatedObject?.Attributes.GetUserString(key);
                if (!string.Equals(actualValue, value ?? string.Empty, StringComparison.Ordinal))
                {
                    results.Add($"[{i}] {id}：提交后校验失败。 ");
                    continue;
                }

                successCount++;
                results.Add($"[{i}] {id}：已写入。 ");
            }

            doc.Views.Redraw();
            DA.SetDataList(0, results);

            if (successCount == rawGuids.Count)
            {
                Message = $"已写入 {successCount}/{rawGuids.Count}";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"UserString批量写入完成：{successCount}/{rawGuids.Count}。");
            }
            else
            {
                Message = $"失败：{successCount}/{rawGuids.Count}";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"UserString批量写入完成：{successCount}/{rawGuids.Count}；请查看Result输出。");
            }
        }

        private void ReportInputError(IGH_DataAccess dataAccess, List<string> results, string error)
        {
            results.Add(error);
            dataAccess.SetDataList(0, results);
            Message = "输入错误";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, error);
        }

        private static bool TryGetGuid(object value, out Guid id)
        {
            id = Guid.Empty;
            if (value is GH_Guid ghGuid)
            {
                id = ghGuid.Value;
                return id != Guid.Empty;
            }

            if (value is Guid guid)
            {
                id = guid;
                return id != Guid.Empty;
            }

            if (value is GH_ObjectWrapper wrapper)
                return TryGetGuid(wrapper.Value, out id);

            return value != null && Guid.TryParse(value.ToString(), out id) && id != Guid.Empty;
        }

        public override void CreateAttributes()
        {
            Attributes = new CButton_SetUserStringList(this);
        }

        protected override Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_GetUserString"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("D599926D-C35A-43CE-AD03-5CFD7594100B"); }
        }
    }

    internal class CButton_SetUserStringList : GH_ComponentAttributes
    {
        public CButton_SetUserStringList(SetUserStringList component) : base(component) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 20.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);
            GH_Palette palette = ((SetUserStringList)Owner).CurrentButtonColor == SetUserStringList.ButtonColor.Black
                ? GH_Palette.Black
                : GH_Palette.Grey;

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, palette))
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (System.Drawing.Font font = new System.Drawing.Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 20, Bounds.Width, 20.0f);
            if (e.Button != MouseButtons.Left || !buttonRect.Contains(e.CanvasLocation))
                return GH_ObjectResponse.Ignore;

            SetUserStringList owner = (SetUserStringList)Owner;
            owner.ButtonRun = true;
            owner.ExpireSolution(true);
            return GH_ObjectResponse.Handled;
        }
    }
}
