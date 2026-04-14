using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;

namespace NS_Parrot
{
    public class SectionParams : GH_PersistentParam<SectionProperty>
    {


        public SectionParams(string name="section", string nickname="截面", string description = "自定义截面信息", 
            string category = "Parrot", string subcategory = "库") : base(name, nickname, description, category, subcategory)
        {
            //base.Name = "";
            //base.NickName = "";
            //base.Description = "";
            //base.Category = "";
            //base.SubCategory = "";
        }

        public override Guid ComponentGuid => Guid.Parse("{2737B093-AE35-40BE-B0BE-6E2D401C8E78}");

        public override string Name { get => base.Name; set => base.Name = value; }
        public override string NickName { get => base.NickName; set => base.NickName = value; }
        public override string Description { get => base.Description; set => base.Description = value; }
        public override string Category { get => base.Category; set => base.Category = value; }
        public override string SubCategory { get => base.SubCategory; set => base.SubCategory = value; }

        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
        }

        public override void AddSource(IGH_Param source)
        {
            base.AddSource(source);
        }

        public override void AddSource(IGH_Param source, int index)
        {
            base.AddSource(source, index);
        }

        public override bool AppendMenuItems(ToolStripDropDown menu)
        {
            return base.AppendMenuItems(menu);
        }

        protected override GH_GetterResult Prompt_Plural(ref List<SectionProperty> values)
        {
            return GH_GetterResult.cancel;
        }

        protected override GH_GetterResult Prompt_Singular(ref SectionProperty value)
        {
            return GH_GetterResult.cancel;
        }
    }
}
