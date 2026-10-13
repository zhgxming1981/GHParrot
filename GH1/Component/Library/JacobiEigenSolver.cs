using Rhino.Geometry;
using System;
using System.Globalization;

namespace NS_Parrot
{
    internal static class JacobiEigenSolver
    {
        public static void SolveSymmetric(double[,] matrix, out double[] eigenValues, out Vector3d[] eigenVectors)
        {
            double[,] a = (double[,])matrix.Clone();
            double[,] v = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                v[i, i] = 1.0;
            }

            // Frobenius 范数（正交变换不变量），用于全流程相对收敛判定
            // 避免绝对 1e-12 在极小尺度矩阵下提前退出
            double frobNorm = Math.Sqrt(
                a[0, 0] * a[0, 0] + a[1, 1] * a[1, 1] + a[2, 2] * a[2, 2] +
                2.0 * (a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2]));

            // 矩阵整体接近零 — 退化分支：任意方向都是特征向量，零为特征值
            if (frobNorm < 1e-300)
            {
                eigenValues = new[] { 0.0, 0.0, 0.0 };
                eigenVectors = new[] { Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis };
                return;
            }

            for (int iteration = 0; iteration < 50; iteration++)
            {
                GetLargestOffDiagonal(a, out int p, out int q, out double max);
                // 相对阈值：非对角元相对于矩阵 Frobenius 范数可忽略时收敛
                if (max < 1e-14 * frobNorm)
                    break;

                double theta = (a[q, q] - a[p, p]) / (2.0 * a[p, q]);
                // Math.Sign(0) == 0 → t=0 导致旋转不执行，等对角元时 Jacobi 失效
                // 此时应执行 45° 旋转，即 sign = +1 → t = 1
                double sign = Math.Sign(theta);
                if (sign == 0) sign = 1.0;
                double t = sign / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                double c = 1.0 / Math.Sqrt(t * t + 1.0);
                double s = t * c;

                Rotate(a, v, p, q, c, s);
            }

            eigenValues = new[] { a[0, 0], a[1, 1], a[2, 2] };
            eigenVectors = new[]
            {
                Normalize(new Vector3d(v[0,0], v[1,0], v[2,0])),
                Normalize(new Vector3d(v[0,1], v[1,1], v[2,1])),
                Normalize(new Vector3d(v[0,2], v[1,2], v[2,2]))
            };

            // 残差校验：|A·v - λ·v| 应相对于矩阵 Frobenius 范数可忽略
            // 避免绝对 1e-8 在小尺度矩阵下放过大相对误差
            for (int i = 0; i < 3; i++)
            {
                double lambda = eigenValues[i];
                Vector3d ev = eigenVectors[i];
                double ax = matrix[0, 0] * ev.X + matrix[0, 1] * ev.Y + matrix[0, 2] * ev.Z;
                double ay = matrix[1, 0] * ev.X + matrix[1, 1] * ev.Y + matrix[1, 2] * ev.Z;
                double az = matrix[2, 0] * ev.X + matrix[2, 1] * ev.Y + matrix[2, 2] * ev.Z;
                double residual = Math.Sqrt(
                    (ax - lambda * ev.X) * (ax - lambda * ev.X) +
                    (ay - lambda * ev.Y) * (ay - lambda * ev.Y) +
                    (az - lambda * ev.Z) * (az - lambda * ev.Z));
                double tol = 1e-8 * frobNorm;
                if (residual > tol)
                    throw new InvalidOperationException(
                        "Jacobi求解器发散：特征值" + lambda.ToString("G6", CultureInfo.InvariantCulture) +
                        "对应残差" + residual.ToString("E2", CultureInfo.InvariantCulture) +
                        "超过阈值" + tol.ToString("E2", CultureInfo.InvariantCulture));
            }
        }

        private static void GetLargestOffDiagonal(double[,] a, out int p, out int q, out double max)
        {
            p = 0;
            q = 1;
            max = Math.Abs(a[0, 1]);

            double value = Math.Abs(a[0, 2]);
            if (value > max)
            {
                p = 0;
                q = 2;
                max = value;
            }

            value = Math.Abs(a[1, 2]);
            if (value > max)
            {
                p = 1;
                q = 2;
                max = value;
            }
        }

        private static void Rotate(double[,] a, double[,] v, int p, int q, double c, double s)
        {
            double app = a[p, p];
            double aqq = a[q, q];
            double apq = a[p, q];

            a[p, p] = c * c * app - 2.0 * s * c * apq + s * s * aqq;
            a[q, q] = s * s * app + 2.0 * s * c * apq + c * c * aqq;
            a[p, q] = 0.0;
            a[q, p] = 0.0;

            for (int r = 0; r < 3; r++)
            {
                if (r == p || r == q)
                    continue;

                double arp = a[r, p];
                double arq = a[r, q];
                a[r, p] = c * arp - s * arq;
                a[p, r] = a[r, p];
                a[r, q] = s * arp + c * arq;
                a[q, r] = a[r, q];
            }

            for (int r = 0; r < 3; r++)
            {
                double vrp = v[r, p];
                double vrq = v[r, q];
                v[r, p] = c * vrp - s * vrq;
                v[r, q] = s * vrp + c * vrq;
            }
        }

        private static Vector3d Normalize(Vector3d vector)
        {
            if (!vector.Unitize())
                return Vector3d.XAxis;

            return vector;
        }
    }
}
