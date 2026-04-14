using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommonFunction.Transform;
using CommonFunction.Hardware;
using CommonFunction.Algorithm;

namespace CommonFunction.Algorithm
{
    public static class CMath
    {
        /// <summary>
        /// 判断2个平面是否共面，原理是先判断法向是否相同，再判断其中一个原点到另一个平面距离是否为0，法向量可以反向
        /// </summary>
        /// <param name="PL1"></param>
        /// <param name="PL2"></param>
        /// <param name="tolerance"></param>
        /// <returns></returns>相同返回1，相反返回-1，距离不同返回2，角度不同返回0
        public static int IsEqPlane(Plane PL1, Plane PL2, double tolerance, out double distance)
        {
            Vector3d v1 = PL1.Normal;//获取平面向量
            Vector3d v2 = PL2.Normal;

            double[] a = { v1.X, v1.Y, v1.Z };
            double[] b = { v2.X, v2.Y, v2.Z };

            for (int i = 0; i < 3; i++)
            {
                a[i] = Math.Round(a[i], 10);
                b[i] = Math.Round(b[i], 10);
            }

            distance = GetDistanceOfPoint2Plane(PL1.Origin, PL2);
            if (distance > tolerance)//判断原点到另一个平面的距离距离
            {
                return 2;//距离不为0，返回2
            }
            else
            {
                int flag = IsEqArray(a, b, tolerance);//判断法向量是否相同或者相反，或者完全不同
                return flag;
            }
        }









        /// <summary>
        /// 比较向量是否等价，原理是先将向量模长统一为100，再比较各项系数
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <param name="tolerance"></param>
        /// <returns></returns>//完全不同返回0，相同返回1，相反返回-1
        public static int IsEqArray(double[] a, double[] b, double tolerance)
        {
            int len = a.Length;

            //先把向量的模长标准化为100，再比较
            double Sa = 0;
            for (int i = 0; i < len; i++)
            {
                Sa += a[i] * a[i];
            }
            double Ka = 100 / Math.Sqrt(Sa);


            double Sb = 0;
            for (int i = 0; i < len; i++)
            {
                Sb += b[i] * b[i];
            }
            double Kb = 100 / Math.Sqrt(Sb);


            for (int i = 0; i < len; i++)
            {
                if (Math.Abs(Math.Abs(Ka * a[i]) - Math.Abs(Kb * b[i])) > tolerance)
                {
                    return 0;//完全不同返回0
                }
            }
            double wc1 = 0, wc2 = 0;//误差
            for (int i = 0; i < len; i++)//判断每项符号是否相同或者相反
            {
                wc1 = wc1 + Math.Abs(Ka * a[i] - Kb * b[i]);
                wc2 = wc2 + Math.Abs(Ka * a[i] + Kb * b[i]);
            }
            if (wc1 < tolerance)
            {
                return 1;//相同返回1
            }
            if (wc2 < tolerance)
            {
                return -1;//相反返回-1
            }
            return 0;//系数绝对值相同，但方向既不相同，也不相反，返回0;
        }


        /// <summary>
        /// 点到平面的距离
        /// </summary>
        /// <param name="point"></param>点
        /// <param name="plane"></param>平面
        /// <returns></returns>
        public static double GetDistanceOfPoint2Plane(Point3d point, Plane plane)
        {
            double A = plane.Normal.X;
            double B = plane.Normal.Y;
            double C = plane.Normal.Z;

            double x0 = plane.Origin.X;
            double y0 = plane.Origin.Y;
            double z0 = plane.Origin.Z;
            double x1 = point.X;
            double y1 = point.Y;
            double z1 = point.Z;
            double D = -A * x0 - B * y0 - C * z0;

            double Dist = Math.Abs(A * x1 + B * y1 + C * z1 + D) / Math.Sqrt(A * A + B * B + C * C);
            return Dist;
        }



        /// <summary>
        /// 延时毫秒数
        /// </summary>
        /// <param name="milliseconds"></param>
        public static void Delay(int milliseconds)
        {
            if (milliseconds <= 0) return;

            DateTime time1 = DateTime.Now;
            int interval = 0;
            do
            {
                TimeSpan spand = DateTime.Now - time1;
                interval = spand.Milliseconds;
                Application.DoEvents();//处理消息队列中的其它消息
            }
            while (interval < milliseconds);
        }

        /// <summary>
        /// 逆时针返回true，顺时针返回false
        /// </summary>
        /// <param name="p1"></param>点1
        /// <param name="p2"></param>点2
        /// <param name="plane"></param>参考面
        /// <returns></returns>
        public static bool? ClockwiseOrAnticlockwise(Point3d p1, Point3d p2, Plane plane)
        {
            Point3d q1 = MyTransform.PointToUCS(p1, plane);
            Point3d q2 = MyTransform.PointToUCS(p2, plane);

            double m = (q2.X - q1.X) * (0 - q1.Y) - (q2.Y - q1.Y) * (0 - q1.X);
            bool? A;
            if (m < 0)
                A = true;
            else if (m > 0)
                A = false;
            else
                A = null;

            return A;
        }

    }
}
