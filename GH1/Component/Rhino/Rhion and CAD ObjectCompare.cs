using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using AutoCAD;
namespace NS_Parrot
{
    public class RhionObjectCompare : IEqualityComparer<GH_Guid>
    {

        bool IEqualityComparer<GH_Guid>.Equals(GH_Guid x, GH_Guid y)
        {
            if (x!=null && y!=null)
            {
                return x.Value == y.Value;
            }
            else
            {
                return false;   
            }
        }

        int IEqualityComparer<GH_Guid>.GetHashCode(GH_Guid obj)
        {
            return obj.Value.GetHashCode();
        }
    }


    //public class AutoCADObjectCompare : IEqualityComparer<Autodesk.AutoCAD.DatabaseServices.ObjectId>
    //{

    //    bool IEqualityComparer<ObjectId>.Equals(ObjectId x, ObjectId y)
    //    {
    //        if (x != null && y != null)
    //        {
    //            return x.Value == y.Value;
    //        }
    //        else
    //        {
    //            return false;
    //        }
    //    }

    //    int IEqualityComparer<GH_Guid>.GetHashCode(ObjectId obj)
    //    {
    //        //obj.
    //        return obj.Value.GetHashCode();
    //    }
    //}

    public class AutoCADEntityCompare : IEqualityComparer<AcadEntity>
    {
        public bool Equals(AcadEntity x, AcadEntity y)
        {
            if (x != null && y != null)
            {
                // 使用 Handle 属性判断是否是同一个 AutoCAD 元素
                return string.Equals(x.Handle, y.Handle, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return false;
            }
        }

        public int GetHashCode(AcadEntity obj)
        {
            if (obj == null) return 0;

            // Handle 是字符串，需要用它生成哈希码
            return obj.Handle != null ? obj.Handle.GetHashCode() : 0;
        }
    }

}
