using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Rhino.Geometry;
namespace CommonFunction.Transform
{
    public class MyTransform
    {
        /// <summary>
        /// 将用户坐标系下的点P1，变换到世界坐标系下
        /// </summary>
        /// <param name="P1"></param>用户坐标系描述的点
        /// <param name="UCS"></param>用户坐标系
        /// <returns></returns>
        public static Point3d PointToWCS(Point3d P1, Plane UCS)
        {
            Vector3d Wx = new Vector3d(1, 0, 0);//世界坐标系的x轴向量
            Vector3d Wy = new Vector3d(0, 1, 0);//世界坐标系的y轴向量
            Vector3d Wz = new Vector3d(0, 0, 1);//世界坐标系的z轴向量

            Vector3d Px = UCS.XAxis;//新平面x轴的向量
            Vector3d Py = UCS.YAxis;//新平面y轴的向量
            Vector3d Pz = UCS.ZAxis;//新平面z轴的向量

            double cos_a1, cos_b1, cos_r1, cos_a2, cos_b2, cos_r2, cos_a3, cos_b3, cos_r3;//x'，y‘，z’轴的单位向量分别在WX，WY，WZ上的投影
            double x0 = UCS.OriginX;//用户坐标的原点
            double y0 = UCS.OriginY;
            double z0 = UCS.OriginZ;

            cos_a1 = DotX(Px, Wx) / Px.Length;//新坐标x'轴与世界坐标x轴的夹角,ux
            cos_b1 = DotX(Py, Wx) / Px.Length;//新坐标x'轴与世界坐标y轴的夹角,uy
            cos_r1 = DotX(Pz, Wx) / Px.Length;//新坐标x'轴与世界坐标z轴的夹角,uz

            cos_a2 = DotX(Px, Wy) / Py.Length;//新坐标y'轴与世界坐标x轴的夹角,vx
            cos_b2 = DotX(Py, Wy) / Py.Length;//新坐标y'轴与世界坐标y轴的夹角,vy
            cos_r2 = DotX(Pz, Wy) / Py.Length;//新坐标y'轴与世界坐标z轴的夹角,vz

            cos_a3 = DotX(Px, Wz) / Pz.Length;//新坐标z'轴与世界坐标x轴的夹角,nx
            cos_b3 = DotX(Py, Wz) / Pz.Length;//新坐标z'轴与世界坐标y轴的夹角,ny
            cos_r3 = DotX(Pz, Wz) / Pz.Length;//新坐标z'轴与世界坐标z轴的夹角,nz

            //坐标变换公式
            double wx, wy, wz;//世界坐标系

            wx = cos_a1 * P1.X + cos_b1 * P1.Y + cos_r1 * P1.Z + x0;
            wy = cos_a2 * P1.X + cos_b2 * P1.Y + cos_r2 * P1.Z + y0;
            wz = cos_a3 * P1.X + cos_b3 * P1.Y + cos_r3 * P1.Z + z0;


            return new Point3d(wx, wy, wz);

        }

        public static Vector3d VectorToWCS(Vector3d V1, Plane UCS)
        {
            Point3d P0s = new Point3d(0, 0, 0);//先将V1平移到世界坐标原点
            Point3d P0e = new Point3d(V1.X, V1.Y, V1.Z);
            Point3d P1s = PointToWCS(P0s, UCS);
            Point3d P1e = PointToWCS(P0e, UCS);

            Vector3d V2 = new Vector3d(P1e.X - P1s.X, P1e.Y - P1s.Y, P1e.Z - P1s.Z);
            return V2;
        }

        public static Point3d PointToUCS(Point3d P1, Plane UCS)
        {
            Vector3d Wx = new Vector3d(1, 0, 0);//世界坐标系的x轴向量
            Vector3d Wy = new Vector3d(0, 1, 0);//世界坐标系的y轴向量
            Vector3d Wz = new Vector3d(0, 0, 1);//世界坐标系的z轴向量

            Vector3d Px = UCS.XAxis;//新平面x轴的向量
            Vector3d Py = UCS.YAxis;//新平面y轴的向量
            Vector3d Pz = UCS.ZAxis;//新平面z轴的向量

            double cos_a1, cos_b1, cos_r1, cos_a2, cos_b2, cos_r2, cos_a3, cos_b3, cos_r3;//x'，y‘，z’轴的单位向量分别在WX，WY，WZ上的投影
            double x0 = UCS.OriginX;//用户坐标的原点
            double y0 = UCS.OriginY;
            double z0 = UCS.OriginZ;
            //cosAX
            cos_a1 = DotX(Px, Wx) / Px.Length;//新坐标x'轴与世界坐标x轴的夹角,ux
            //cosBX
            cos_b1 = DotX(Px, Wy) / Px.Length;//新坐标x'轴与世界坐标y轴的夹角,uy
            //cosCX
            cos_r1 = DotX(Px, Wz) / Px.Length;//新坐标x'轴与世界坐标z轴的夹角,uz

            //cosAY
            cos_a2 = DotX(Py, Wx) / Py.Length;//新坐标y'轴与世界坐标x轴的夹角,vx
            //cosBY
            cos_b2 = DotX(Py, Wy) / Py.Length;//新坐标y'轴与世界坐标y轴的夹角,vy
            //cosCY
            cos_r2 = DotX(Py, Wz) / Py.Length;//新坐标y'轴与世界坐标z轴的夹角,vz

            //cosAZ
            cos_a3 = DotX(Pz, Wx) / Pz.Length;//新坐标z'轴与世界坐标x轴的夹角,nx
            //cosBZ
            cos_b3 = DotX(Pz, Wy) / Pz.Length;//新坐标z'轴与世界坐标y轴的夹角,ny
            //cosCZ
            cos_r3 = DotX(Pz, Wz) / Pz.Length;//新坐标z'轴与世界坐标z轴的夹角,nz

            //坐标变换公式
            double ux, uy, uz;//用户坐标系
            ux = cos_a1 * (P1.X - x0) + cos_b1 * (P1.Y - y0) + cos_r1 * (P1.Z - z0);
            uy = cos_a2 * (P1.X - x0) + cos_b2 * (P1.Y - y0) + cos_r2 * (P1.Z - z0);
            uz = cos_a3 * (P1.X - x0) + cos_b3 * (P1.Y - y0) + cos_r3 * (P1.Z - z0);

            return new Point3d(ux, uy, uz);
        }

        public static Vector3d VectorToUCS(Vector3d V1, Plane UCS)
        {
            Point3d P0s = new Point3d(0, 0, 0);//先将V1平移到世界坐标原点
            Point3d P0e = new Point3d(V1.X, V1.Y, V1.Z);
            Point3d P1s = PointToUCS(P0s, UCS);
            Point3d P1e = PointToUCS(P0e, UCS);

            Vector3d V2 = new Vector3d(P1e.X - P1s.X, P1e.Y - P1s.Y, P1e.Z - P1s.Z);
            return V2;
        }

        public static double DotX(Vector3d a, Vector3d b) //向量点乘
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }
    }
}
