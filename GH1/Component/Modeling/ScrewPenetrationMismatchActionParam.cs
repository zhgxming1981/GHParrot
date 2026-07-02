using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class ScrewPenetrationMismatchActionParam : Param_String
    {
        public const string DefaultAction = "默认";
        public const string InvalidAction = "无效";

        public ScrewPenetrationMismatchActionParam()
        {
            Name = "处理方式";
            NickName = "处理方式";
            Description = "穿透层数与预期不符时的处理方式：默认=继续按默认规则开孔；无效=此螺丝无效并跳过";
            Access = GH_ParamAccess.item;
            Optional = true;
        }

        public override Guid ComponentGuid => new Guid("41E62B46-7B9A-44D2-8C0A-FC21D4B67648");

        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, DefaultAction, (sender, args) => SetAction(DefaultAction), true, HasAction(DefaultAction));
            Menu_AppendItem(menu, InvalidAction, (sender, args) => SetAction(InvalidAction), true, HasAction(InvalidAction));
        }

        private void SetAction(string action)
        {
            RecordUndoEvent("设置处理方式");
            PersistentData.Clear();
            PersistentData.Append(new GH_String(action));
            OnObjectChanged(GH_ObjectEventType.PersistentData);
            ExpireSolution(true);
        }

        private bool HasAction(string action)
        {
            if (PersistentData == null || PersistentData.DataCount == 0)
                return string.Equals(action, DefaultAction, StringComparison.OrdinalIgnoreCase);

            foreach (GH_String item in PersistentData.AllData(true))
            {
                string value = item?.Value;
                if (string.Equals(value, action, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
