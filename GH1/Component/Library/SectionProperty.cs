using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Grasshopper.Kernel;
namespace NS_Parrot
{
    public class SectionProperty : IGH_Goo
    {
        private List<Curve> section;
        private string name1;//厂家名称
        private string name2;//自定义名称
        private string material;//材质
        private string colorName;
        private string appearance;//表面处理
        private double section_area;//截面积
        private double density;//密度
        //private int index_section = 0;//送货地址
        private string description;

        public SectionProperty()
        {
        }

        public SectionProperty(List<Curve> section, string name1 = "", string name2 = "", string material = "6063-T5", string colorName = "",
          string appearance = "氟碳喷涂", double section_area = 0, double density = 0, string description = "")
        {
            this.section = section;
            this.name1 = name1;//厂家名称
            this.name2 = name2;//自定义名称
            this.material = material;
            this.colorName = colorName;
            this.appearance = appearance;
            this.section_area = section_area;
            this.density = density;
            this.description = description;
        }

        bool IGH_Goo.IsValid => throw new NotImplementedException();

        string IGH_Goo.IsValidWhyNot => throw new NotImplementedException();

        string IGH_Goo.TypeName => nameof(SectionProperty);

        string IGH_Goo.TypeDescription => throw new NotImplementedException();

        public override string ToString()
        {
            var retVal = new StringBuilder();
            retVal.AppendLine("厂家名称:" + name1);
            retVal.AppendLine("自定义名称:" + name2);
            retVal.AppendLine("材质:" + material);
            retVal.AppendLine("表面处理:" + appearance);
            retVal.AppendLine("颜色:" + colorName);
            retVal.AppendLine("截面积:" + section_area);
            retVal.AppendLine("密度:" + density);
            retVal.AppendLine("备注:" + description);
            return retVal.ToString();
        }

        bool IGH_Goo.CastFrom(object source)
        {
            throw new NotImplementedException();
        }

        bool IGH_Goo.CastTo<T>(out T target)
        {
            if (typeof(T) == typeof(GH_Curve))
            {
                GH_Curve c = new GH_Curve(section[0]);//此处有问题，只能转换一个截面，否则就出错
                if (c != null)
                {
                    target = (T)(object)c;
                    return true;
                }

                //List<GH_Curve> curves = new List<GH_Curve>();
                //int count = section.Count;
                //foreach (var curve in section)
                //{
                //    curves.Add(new GH_Curve(curve));
                //}

                //if(curves.Count > 0)
                //{
                //    target = (T)(object)curves;
                //    return true;
                //}

            }

            target = default(T);
            return false;
        }

        IGH_Goo IGH_Goo.Duplicate()
        {
            throw new NotImplementedException();
        }

        IGH_GooProxy IGH_Goo.EmitProxy()
        {
            throw new NotImplementedException();
        }

        bool GH_ISerializable.Read(GH_IReader reader)
        {
            throw new NotImplementedException();
        }

        object IGH_Goo.ScriptVariable()
        {
            throw new NotImplementedException();
        }

        bool GH_ISerializable.Write(GH_IWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}
