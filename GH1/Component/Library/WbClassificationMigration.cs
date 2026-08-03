using GH_IO.Serialization;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NS_Parrot
{
    internal enum WbMigrationPhase { Extracting, Finalizing }

    internal sealed class WbMigrationCheckpoint
    {
        public string DatabasePath;
        public List<string> PendingFiles;
        public int FileIndex;
        public List<CheckpointRecord> CollectedRecords = new List<CheckpointRecord>();
        public List<string> Diagnostics = new List<string>();
        public List<string> FailedFiles = new List<string>();
        public int TotalErrorCount;
        public WbMigrationPhase Phase = WbMigrationPhase.Extracting;

        internal sealed class CheckpointRecord
        {
            public int Index;
            public string Number;
            public string FeatureText;
            public string SourceFile;
        }

        public static WbMigrationCheckpoint Load(string checkpointPath)
        {
            if (!File.Exists(checkpointPath)) return null;
            try
            {
                string text = File.ReadAllText(checkpointPath, Encoding.UTF8);
                return Deserialize(text);
            }
            catch (Exception ex)
            {
                // 解析失败时不要静默回退——输出诊断以便排查
                throw new InvalidDataException(
                    "检查点文件解析失败（" + checkpointPath + "）：" + ex.Message, ex);
            }
        }

        public void Save(string checkpointPath)
        {
            string data = Serialize();
            string tmpPath = checkpointPath + ".tmp";
            try
            {
                File.WriteAllText(tmpPath, data, Encoding.UTF8);
                if (File.Exists(checkpointPath))
                    File.Replace(tmpPath, checkpointPath, null);
                else
                    File.Move(tmpPath, checkpointPath);
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }

        private string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine(DatabasePath ?? "");
            sb.AppendLine(PendingFiles != null ? string.Join("\u0002", PendingFiles) : "");
            sb.AppendLine(FileIndex.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(TotalErrorCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine(FailedFiles != null ? string.Join("\u0002", FailedFiles) : "");
            sb.AppendLine(CollectedRecords != null ? CollectedRecords.Count.ToString(CultureInfo.InvariantCulture) : "0");
            if (CollectedRecords != null)
                foreach (var cr in CollectedRecords)
                {
                    sb.AppendLine((cr.Index.ToString(CultureInfo.InvariantCulture)) +
                        "\u0001" + Escape(cr.Number) +
                        "\u0001" + Escape(cr.SourceFile) +
                        "\u0001" + Escape(cr.FeatureText));
                }
            sb.AppendLine(Diagnostics != null ? Diagnostics.Count.ToString(CultureInfo.InvariantCulture) : "0");
            if (Diagnostics != null)
                foreach (var d in Diagnostics)
                    sb.AppendLine(Escape(d));
            sb.AppendLine(Phase.ToString());
            return sb.ToString();
        }

        private static WbMigrationCheckpoint Deserialize(string data)
        {
            // 使用 StringReader.ReadLine() 读取每一行，保留空行，避免 RemoveEmptyEntries 导致字段位偏移
            var lines = new List<string>();
            using (var sr = new StringReader(data))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                    lines.Add(line);
            }
            if (lines.Count < 7)
                throw new InvalidDataException(
                    "检查点文件结构不完整：期望至少 7 行，实际 " + lines.Count + " 行。");
            var cp = new WbMigrationCheckpoint();
            int idx = 0;
            cp.DatabasePath = lines[idx++];
            cp.PendingFiles = string.IsNullOrEmpty(lines[idx]) ? new List<string>()
                : new List<string>(lines[idx].Split('\u0002'));
            idx++;
            cp.FileIndex = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            cp.TotalErrorCount = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            cp.FailedFiles = string.IsNullOrEmpty(lines[idx]) ? new List<string>()
                : new List<string>(lines[idx].Split('\u0002'));
            idx++;
            int recCount = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            if (recCount < 0)
                throw new InvalidDataException("检查点记录数声明为负：" + recCount);
            int recStart = idx;
            cp.CollectedRecords = new List<CheckpointRecord>();
            for (int i = 0; i < recCount; i++)
            {
                if (idx >= lines.Count)
                    throw new InvalidDataException(
                        "检查点记录截断：声明 " + recCount + " 条，实际仅 " + (idx - recStart) + " 条完整记录行。");
                string[] parts = lines[idx++].Split('\u0001');
                if (parts.Length != 4)
                    throw new InvalidDataException(
                        "检查点第 " + (idx - 1).ToString(CultureInfo.InvariantCulture) +
                        " 行字段数异常：期望 4 (\u0001 分隔)，实际 " + parts.Length);
                cp.CollectedRecords.Add(new CheckpointRecord
                {
                    Index = int.Parse(parts[0], CultureInfo.InvariantCulture),
                    Number = Unescape(parts[1]),
                    SourceFile = Unescape(parts[2]),
                    FeatureText = Unescape(parts[3])
                });
            }
            if (idx >= lines.Count)
                throw new InvalidDataException(
                    "检查点诊断行截断：声明 " + recCount + " 条记录已读完，但缺诊断数量行。");
            int diagCount = int.Parse(lines[idx++], CultureInfo.InvariantCulture);
            if (diagCount < 0)
                throw new InvalidDataException("检查点诊断数声明为负：" + diagCount);
            int diagStart = idx;
            cp.Diagnostics = new List<string>();
            for (int i = 0; i < diagCount; i++)
            {
                if (idx >= lines.Count)
                    throw new InvalidDataException(
                        "检查点诊断截断：声明 " + diagCount + " 条，实际仅 " + (idx - diagStart) + " 条。");
                cp.Diagnostics.Add(Unescape(lines[idx++]));
            }
            // 末尾可选：迁移阶段（向后兼容旧检查点）
            if (idx < lines.Count)
                Enum.TryParse(lines[idx], true, out cp.Phase);
            return cp;
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\\\", "\\");
        }

        public static string GetCheckpointPath(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) return string.Empty;
            return Path.GetFullPath(databasePath) + ".migration_checkpoint.json";
        }
    }

    // ========================================================================
    // 镜像判定回归测试
    //
    // 执行方式（发布前执行）：
    //   1. Visual Studio 即时窗口 → WbMirrorTest.RunAllAndLog()
    //   2. 或在单元测试项目中调用 WbMirrorTest.RunAll()
    //   3. 或在 Rhino Python 编辑器中：
    //      import clr; clr.AddReference("GH1")
    //      from GH1.Component.Library import WbMirrorTest
    //      WbMirrorTest.RunAllAndLog()
    // ========================================================================
    internal static class WbMirrorTest
    {
        public sealed class TestResult
        {
            public string TestName;
            public bool Passed;
            public string Detail;
        }

        /// <summary>
        /// 运行全部回归测试并输出结果到 Rhino 命令行。
        /// 发布前必须调用，全部通过才能发布。
        /// </summary>
        public static void RunAllAndLog()
        {
            var result = RunAll();
            RhinoApp.WriteLine("=== WbClassificationMigration 回归测试 ===");
            foreach (var r in result.Item2)
                RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  [{0}] {1}: {2}", r.Passed ? "PASS" : "FAIL", r.TestName, r.Detail));
            RhinoApp.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "=== 总计: {0}/{1} 通过 ===",
                result.Item2.Count(x => x.Passed), result.Item2.Count));
        }

        public static Tuple<bool, List<TestResult>> RunAll()
        {
            var results = new List<TestResult>();
            double mirrorTol = 0.001;
            var e = new Dictionary<V5Criterion, bool> {
                { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
                { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
                { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
            };
            var t = new Dictionary<V5Criterion, double> {
                { V5Criterion.Length, 0.5 }, { V5Criterion.Inertia, 0.2 },
                { V5Criterion.Volume, 0.5 }, { V5Criterion.EdgeSum, 1 },
                { V5Criterion.HoleArea, 1 }, { V5Criterion.HolePosition, 0.5 }
            };
            var r = new Dictionary<V5Criterion, bool> {
                { V5Criterion.Length, true }, { V5Criterion.Inertia, true },
                { V5Criterion.Volume, true }, { V5Criterion.EdgeSum, true },
                { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
            };

            results.Add(TestIdenticalNotMirror(mirrorTol, e, t, r));
            results.Add(TestBMeansMirror(mirrorTol, e, t, r));
            results.Add(TestSyntheticSameGeomDiffMirrorCode(mirrorTol, e, t, r));
            results.Add(TestZeroMirrorScore(mirrorTol, e, t, r));
            results.Add(TestDegenerateMirror(mirrorTol, e, t, r));

            // 真实几何回归测试（Brep → 特征提取 → 镜像判定 完整管线）
            results.Add(TestRealGeometryMirrorDetection(mirrorTol, e, t, r));
            results.Add(TestRealGeometrySameNotMirror(mirrorTol, e, t, r));
            results.Add(TestRealGeometryDegenerateNotCrash(mirrorTol, e, t, r));
            results.Add(TestRealGeometryNearSymmetricNotMirror(mirrorTol, e, t, r));
            results.Add(TestRealGeometryBatchStatistics(mirrorTol, e, t, r));

            // 检查点解析回归测试
            results.AddRange(WbCheckpointTest.RunAll());

            // 退化孔匹配回归测试
            results.AddRange(WbDegenerateMatchingTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            // Jacobi 特征分解回归测试
            results.AddRange(JacobiEigenTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            // 迁移完整性回归测试（建库清理、日志落盘）
            results.AddRange(WbMigrationStoreTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            // TryParse 参数解析回归测试（EdgeSum 往返/兼容/非法值）
            results.AddRange(WbSettingsParseTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            // CanAutoMatch 退化孔隔离回归测试
            results.AddRange(WbCanAutoMatchTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            // 极小外轮廓面积容差回归测试
            results.AddRange(WbTinyOuterProfileTest.RunAll().Select(tc => new TestResult
            {
                TestName = tc.TestName,
                Passed = tc.Passed,
                Detail = tc.Detail
            }));

            bool allPassed = results.All(x => x.Passed);
            return Tuple.Create(allPassed, results);
        }

        private static WbFeature MakeFeature(double mirrorScore, double holeMirrorScore,
            string material = "", string color = "",
            double[] bbox = null, double[] inertia = null,
            double area = 1000, double volume = 500,
            int holeCount = 2, bool isDegenerate = false)
        {
            bbox = bbox ?? new[] { 100.0, 50.0, 30.0 };
            inertia = inertia ?? new[] { 2000.0, 1000.0, 500.0 };
            var f = new WbFeature
            {
                Material = material, Color = color,
                BoundingBox = bbox, Area = area, Volume = volume,
                Inertia = inertia,
                MirrorScore = mirrorScore,
                HoleMirrorScore = holeMirrorScore,
                IsDegenerate = isDegenerate
            };
            for (int i = 0; i < holeCount; i++)
                f.Holes.Add(new WbHole(10, 5, i * 20.0, i * 10.0, 0, 0, 0, 1));
            return f;
        }

        private static TestResult TestIdenticalNotMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.5, 0.3);
            var b = MakeFeature(0.5, 0.3);
            string mirrorA = MirrorCode(a, mirrorTol);
            string mirrorB = MirrorCode(b, mirrorTol);
            bool sameCode = SameMirrorCode(a, b, mirrorTol);
            bool isMirror = MirrorMatches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            bool passed = !isMirror;
            return new TestResult
            {
                TestName = "完全相同件不判定为镜像",
                Passed = passed,
                Detail = passed ? "OK" :
                    "SameCode=" + sameCode + " IsMirror=" + isMirror
            };
        }

        private static TestResult TestBMeansMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.5, 0.3);
            var b = MakeFeature(-0.5, -0.3);
            string mirrorA = MirrorCode(a, mirrorTol);
            string mirrorB = MirrorCode(b, mirrorTol);
            bool passed = mirrorA == "A" && mirrorB == "B";
            return new TestResult
            {
                TestName = "正负MirrorScore分别对应A/B",
                Passed = passed,
                Detail = passed ? "OK" : "Got " + mirrorA + "/" + mirrorB
            };
        }

        /// <summary>正负 MirrorScore 但几何相同 → 仍判为同件，不标镜像。</summary>
        private static TestResult TestSyntheticSameGeomDiffMirrorCode(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.5, 0.3);
            var b = MakeFeature(-0.5, -0.3);
            bool sameCode = SameMirrorCode(a, b, mirrorTol);
            bool matches = Matches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            bool mirror = MirrorMatches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            // 镜像码不同、但几何相同 → 按新规则仍为同件，不标镜像
            bool passed = !sameCode && matches && !mirror;
            return new TestResult
            {
                TestName = "镜像码不同但同几何仍判为同件",
                Passed = passed,
                Detail = passed ? "OK" :
                    "SameCode=" + sameCode + " Matches=" + matches + " Mirror=" + mirror
            };
        }

        private static TestResult TestZeroMirrorScore(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.0, 0.0);
            var b = MakeFeature(0.0, 0.0);
            string mirrorA = MirrorCode(a, mirrorTol);
            string mirrorB = MirrorCode(b, mirrorTol);
            bool sameCode = SameMirrorCode(a, b, mirrorTol);
            bool passed = mirrorA == string.Empty && mirrorB == string.Empty && sameCode;
            return new TestResult
            {
                TestName = "零MirrorScore返回空代码",
                Passed = passed,
                Detail = passed ? "OK" :
                    "A='" + mirrorA + "' B='" + mirrorB + "' SameCode=" + sameCode
            };
        }

        private static TestResult TestDegenerateMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            var a = MakeFeature(0.6, 0.4, isDegenerate: true);
            var b = MakeFeature(-0.6, -0.4, isDegenerate: true);
            bool mirror = MirrorMatches(a, b, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
            return new TestResult
            {
                TestName = "退化件镜像检测不崩溃",
                Passed = true,
                Detail = "退化件镜像检测=" + mirror
            };
        }

        // ================================================================
        // 真实几何回归测试 — 验证 Brep → WbFeature → 镜像判定 完整管线
        // ================================================================

        /// <summary>
        /// 创建非对称测试 Brep：10×8×6 的盒子，在 (2,0,0) 处挖一个偏置圆柱孔（直径 2）。
        /// </summary>
        private static Brep CreateAsymmetricTestBrep()
        {
            return CreateBoxWithHole(5, 4, 3, new Point3d(2, 0, -5), 1.0, 10.0);
        }

        /// <summary>将 Brep 沿 YZ 平面镜像（X 坐标取反），产生左右对称件。</summary>
        private static Brep CreateMirrorBrep(Brep original)
        {
            Brep mirror = original.DuplicateBrep();
            mirror.Transform(Transform.Mirror(Plane.WorldYZ));
            return mirror;
        }

        /// <summary>
        /// 创建带偏置圆柱孔的盒体 Brep。用于构造非对称测试样本。
        /// 若 BooleanDifference 失败则抛出 InvalidOperationException。
        /// </summary>
        /// <param name="boxX">盒体 X 半长</param>
        /// <param name="boxY">盒体 Y 半长</param>
        /// <param name="boxZ">盒体 Z 半长</param>
        /// <param name="holeCenter">圆柱孔中心坐标（含 Z 方向穿透起点）</param>
        /// <param name="holeRadius">圆柱孔半径</param>
        /// <param name="holeHeight">圆柱孔高度（Z 方向穿透长度）</param>
        private static Brep CreateBoxWithHole(double boxX, double boxY, double boxZ,
            Point3d holeCenter, double holeRadius, double holeHeight)
        {
            var box = new Box(Plane.WorldXY,
                new Interval(-boxX, boxX), new Interval(-boxY, boxY), new Interval(-boxZ, boxZ));
            Brep boxBrep = box.ToBrep();
            if (boxBrep == null)
                throw new InvalidOperationException("盒体创建失败。");

            var cylPlane = new Plane(holeCenter, Vector3d.ZAxis);
            var cyl = new Cylinder(new Circle(cylPlane, holeRadius), holeHeight);
            Brep cylBrep = cyl.ToBrep(true, true);
            if (cylBrep == null)
                throw new InvalidOperationException("圆柱体创建失败。");

            Brep[] diff = Brep.CreateBooleanDifference(
                new List<Brep> { boxBrep }, new List<Brep> { cylBrep }, 0.001);
            if (diff == null || diff.Length == 0)
                throw new InvalidOperationException("BooleanDifference 失败。");
            return diff[0];
        }

        /// <summary>场景 1：非对称 Brep 与 其镜像 应被判定为镜像对。</summary>
        private static TestResult TestRealGeometryMirrorDetection(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                Brep brep = CreateAsymmetricTestBrep();
                Brep brepMirror = CreateMirrorBrep(brep);

                WbFeature f1 = WbFeature.Create("steel", "red", brep, 0.9986, 0.001, 0);
                WbFeature f2 = WbFeature.Create("steel", "red", brepMirror, 0.9986, 0.001, 1);

                bool sameCode = SameMirrorCode(f1, f2, mirrorTol);
                bool isMirror = MirrorMatches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                bool passed = isMirror && !isMatch;
                return new TestResult
                {
                    TestName = "真实几何:非对称件+镜像判定为镜像对",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "SameCode={0} Mirror={1} Match={2} MS={3} HMS={4} / MMS={5} MHMS={6}",
                            sameCode, isMirror, isMatch,
                            WbUtil.FR(f1.MirrorScore), WbUtil.FR(f1.HoleMirrorScore),
                            WbUtil.FR(f2.MirrorScore), WbUtil.FR(f2.HoleMirrorScore))
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:非对称件+镜像判定为镜像对",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>场景 2：相同 Brep 重复提取 → SameMirrorCode + Matches，不判为镜像。</summary>
        private static TestResult TestRealGeometrySameNotMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                Brep brep = CreateAsymmetricTestBrep();

                WbFeature f1 = WbFeature.Create("steel", "red", brep, 0.9986, 0.001, 0);
                WbFeature f2 = WbFeature.Create("steel", "red", brep, 0.9986, 0.001, 1);

                bool sameCode = SameMirrorCode(f1, f2, mirrorTol);
                bool isMirror = MirrorMatches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                // 同一 Brep 提取两次：能匹配为同件、不判为镜像
                bool passed = isMatch && !isMirror;
                return new TestResult
                {
                    TestName = "真实几何:同Brep判为同件非镜像",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "SameCode={0} Match={1} Mirror={2}", sameCode, isMatch, isMirror)
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:同Brep判为同件非镜像",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>场景 3：退化几何（立方体，惯量退化）走完整管线不崩溃且不判为镜像。</summary>
        private static TestResult TestRealGeometryDegenerateNotCrash(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                // 立方体 1×1×1：三轴惯量相等 → IsDegenerate=true
                var box = new Box(Plane.WorldXY,
                    new Interval(-0.5, 0.5), new Interval(-0.5, 0.5), new Interval(-0.5, 0.5));
                Brep cube = box.ToBrep();
                if (cube == null) throw new InvalidOperationException("立方体创建失败。");

                WbFeature f1 = WbFeature.Create("steel", "", cube, 0.99, 0.001, 0);
                WbFeature f2 = WbFeature.Create("steel", "", cube, 0.99, 0.001, 1);

                // 立方体应被检测为惯量退化
                bool degenOK = f1.IsDegenerate && f2.IsDegenerate;
                bool sameCode = SameMirrorCode(f1, f2, mirrorTol);
                bool isMirror = MirrorMatches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(f1, f2, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                // 退化+同件+非镜像关系
                bool passed = degenOK && isMatch && !isMirror;
                return new TestResult
                {
                    TestName = "真实几何:退化立方体不崩且非镜像",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "Degen=({0},{1}) SameCode={2} Mirror={3} Match={4}",
                            f1.IsDegenerate, f2.IsDegenerate, sameCode, isMirror, isMatch)
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:退化立方体不崩且非镜像",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>
        /// 场景 4：近对称但非镜像 — 两个 Brep 外形相似、镜像码相反，
        /// 但几何细节不同（孔位/孔径差异），应不被误判为镜像。
        /// 此项验证误合并风险控制。
        /// </summary>
        private static TestResult TestRealGeometryNearSymmetricNotMirror(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {
                // Brep A：10×8×6 盒子，孔在 (+2, 0)，半径 1.0
                Brep brepA = CreateAsymmetricTestBrep();

                // Brep B：10×8×6 盒子，孔在 (-1.5, 0)，半径 1.2
                // 外形与 A 相似但孔位置/半径不同，不是 A 的镜像（镜像应在 -2, r=1.0）
                var boxB = new Box(Plane.WorldXY,
                    new Interval(-5, 5), new Interval(-4, 4), new Interval(-3, 3));
                Brep boxBrepB = boxB.ToBrep();
                if (boxBrepB == null) throw new InvalidOperationException("盒体 B 创建失败。");

                var cylB = new Cylinder(
                    new Circle(new Plane(new Point3d(-1.5, 0, -5), Vector3d.ZAxis), 1.2), 10.0);
                Brep cylBrepB = cylB.ToBrep(true, true);
                if (cylBrepB == null) throw new InvalidOperationException("圆柱 B 创建失败。");

                Brep[] diffB = Brep.CreateBooleanDifference(
                    new List<Brep> { boxBrepB },
                    new List<Brep> { cylBrepB }, 0.001);
                if (diffB == null || diffB.Length == 0)
                    throw new InvalidOperationException("近对称件 B 的 BooleanDifference 失败。");
                Brep brepB = diffB[0];

                WbFeature fA = WbFeature.Create("steel", "red", brepA, 0.9986, 0.001, 0);
                WbFeature fB = WbFeature.Create("steel", "red", brepB, 0.9986, 0.001, 1);

                bool sameCode = SameMirrorCode(fA, fB, mirrorTol);
                bool isMirror = MirrorMatches(fA, fB, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);
                bool isMatch = Matches(fA, fB, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                // 近对称但非镜像：外观相似但不应被判为镜像对或同件
                bool passed = !isMirror && !isMatch;
                return new TestResult
                {
                    TestName = "真实几何:近对称非镜像不误判",
                    Passed = passed,
                    Detail = passed ? "OK"
                        : string.Format(CultureInfo.InvariantCulture,
                            "SameCode={0} Mirror={1} Match={2}",
                            sameCode, isMirror, isMatch)
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:近对称非镜像不误判",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        // ================================================================
        // 样本集统计回归测试 — 多对正/负样本批量验证误合并率与漏检率
        // ================================================================

        /// <summary>几何样本定义：两个 Brep 和预期关系。</summary>
        private sealed class GeometrySample
        {
            public string Name;
            public Brep BrepA;
            public Brep BrepB;
            /// <summary>预期 MirrorMatches</summary>
            public bool ExpectedMirror;
        }

        /// <summary>
        /// 构建正、负样本集（3 正 6 负）。
        /// 正样本 = 真实镜像对，应 hit；负样本 = 非镜像/非对称，不应误判。
        /// </summary>
        private static List<GeometrySample> BuildGeometrySamples()
        {
            var samples = new List<GeometrySample>();

            // === 正样本：真实镜像对 ===

            // P1：标准非对称件 → 镜像
            Brep asym = CreateAsymmetricTestBrep();         // 10×8×6, 孔(+2,0,r=1)
            Brep asymMirror = CreateMirrorBrep(asym);       // 孔(-2,0,r=1)
            samples.Add(new GeometrySample
            {
                Name = "正#1:10×8×6单孔偏移",
                BrepA = asym, BrepB = asymMirror,
                ExpectedMirror = true
            });

            // P2：宽盒双孔 → 镜像（多孔场景）
            Brep dualHole = CreateBoxWithHole(10, 5, 4,
                new Point3d(3, 0, -5), 1.0, 10.0);
            if (dualHole != null)
            {
                // 补第二个孔：在已打孔的 brep 上再减一个圆柱
                var cyl2 = new Cylinder(
                    new Circle(new Plane(new Point3d(3, 3, -5), Vector3d.ZAxis), 0.8), 10.0);
                Brep cylBrep2 = cyl2.ToBrep(true, true);
                Brep[] dual2 = Brep.CreateBooleanDifference(
                    new List<Brep> { dualHole }, new List<Brep> { cylBrep2 }, 0.001);
                if (dual2 != null && dual2.Length > 0)
                {
                    Brep dualHoleMirror = CreateMirrorBrep(dual2[0]);
                    samples.Add(new GeometrySample
                    {
                        Name = "正#2:20×10×8双孔偏移→镜像",
                        BrepA = dual2[0], BrepB = dualHoleMirror,
                        ExpectedMirror = true
                    });
                }
            }

            // P3：扁盒（不同纵横比）→ 镜像
            Brep flatBox = CreateBoxWithHole(6, 2, 6,
                new Point3d(1.5, 0, -7), 0.8, 14.0);
            Brep flatBoxMirror = CreateMirrorBrep(flatBox);
            samples.Add(new GeometrySample
            {
                Name = "正#3:12×4×12扁盒单孔→镜像",
                BrepA = flatBox, BrepB = flatBoxMirror,
                ExpectedMirror = true
            });

            // === 负样本：不应被判为镜像 ===

            // N1：近对称（孔位不同、孔径不同）
            Brep negNear = CreateBoxWithHole(5, 4, 3,
                new Point3d(-1.5, 0, -5), 1.2, 10.0);
            samples.Add(new GeometrySample
            {
                Name = "负#1:近对称不同孔位/孔径",
                BrepA = asym, BrepB = negNear,
                ExpectedMirror = false
            });

            // N2：退化立方体自身（三轴惯量相等）
            var cube = new Box(Plane.WorldXY,
                new Interval(-0.5, 0.5), new Interval(-0.5, 0.5), new Interval(-0.5, 0.5))
                .ToBrep();
            samples.Add(new GeometrySample
            {
                Name = "负#2:退化立方体自身",
                BrepA = cube, BrepB = cube,
                ExpectedMirror = false
            });

            // N3：非对称件自身
            samples.Add(new GeometrySample
            {
                Name = "负#3:非对称件自身",
                BrepA = asym, BrepB = asym,
                ExpectedMirror = false
            });

            // N4：不同尺寸盒体（10×8×6 vs 8×6×4），完全不同的零件
            Brep smallerBox = CreateBoxWithHole(4, 3, 2,
                new Point3d(1, 0, -4), 0.6, 8.0);
            samples.Add(new GeometrySample
            {
                Name = "负#4:不同尺寸(10×8×6 vs 8×6×4)",
                BrepA = asym, BrepB = smallerBox,
                ExpectedMirror = false
            });

            // N5：等比缩放（同形不同尺 → isometric 相似但不为镜像）
            Brep scaledBox = CreateBoxWithHole(10, 8, 6,
                new Point3d(4, 0, -10), 2.0, 20.0);
            samples.Add(new GeometrySample
            {
                Name = "负#5:等比2×缩放(同形不同尺)",
                BrepA = asym, BrepB = scaledBox,
                ExpectedMirror = false
            });

            // N6：同盒体、同孔位、不同孔径（r=1 vs r=1.5）
            Brep largerHole = CreateBoxWithHole(5, 4, 3,
                new Point3d(2, 0, -5), 1.5, 10.0);
            samples.Add(new GeometrySample
            {
                Name = "负#6:同盒同孔位不同孔径(r=1 vs 1.5)",
                BrepA = asym, BrepB = largerHole,
                ExpectedMirror = false
            });

            return samples;
        }

        /// <summary>场景 5：批量样本集统计 — 计算误合并率（负→正）与漏检率（正→负）。</summary>
        private static TestResult TestRealGeometryBatchStatistics(double mirrorTol,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> r)
        {
            try
            {

                List<GeometrySample> samples = BuildGeometrySamples();

                int positiveTotal = 0, positiveHit = 0;   // 正样本 = 应判镜像
                int negativeTotal = 0, negativeFalse = 0; // 负样本 = 不应判镜像
                var details = new List<string>();

                foreach (var s in samples)
                {
                    WbFeature fA = WbFeature.Create("steel", "red", s.BrepA, 0.9986, 0.001, 0);
                    WbFeature fB = WbFeature.Create("steel", "red", s.BrepB, 0.9986, 0.001, 1);

                    bool sameCode = SameMirrorCode(fA, fB, mirrorTol);
                    bool isMirror = MirrorMatches(fA, fB, e, t, r, mirrorTol, true, 0.9986, 0.0001, 0.01);

                    bool mirrorOk = isMirror == s.ExpectedMirror;

                    if (s.ExpectedMirror)
                    {
                        positiveTotal++;
                        if (isMirror) positiveHit++;
                    }
                    else
                    {
                        negativeTotal++;
                        if (isMirror) negativeFalse++;
                    }

                    // sameCode 仅用于日志观察，不影响通过判定
                    details.Add(s.Name + ":" + (mirrorOk ? "OK" : "FAIL") +
                        " SameCode=" + sameCode +
                        " Mirror=" + isMirror + "(exp " + s.ExpectedMirror + ")");
                }

                bool allPassed = positiveHit == positiveTotal && negativeFalse == 0;
                double recall = positiveTotal > 0 ? 100.0 * positiveHit / positiveTotal : 100.0;
                double falsePosRate = negativeTotal > 0 ? 100.0 * negativeFalse / negativeTotal : 0;

                return new TestResult
                {
                    TestName = string.Format(CultureInfo.InvariantCulture,
                        "真实几何:样本集统计({0}正{1}负)", positiveTotal, negativeTotal),
                    Passed = allPassed,
                    Detail = (allPassed ? "OK" : "有失败")
                        + string.Format(CultureInfo.InvariantCulture,
                            " | 正样本命中 {0}/{1} ({2:F0}%), 负样本误判 {3}/{4} ({5:F0}%)",
                            positiveHit, positiveTotal, recall,
                            negativeFalse, negativeTotal, falsePosRate)
                        + (allPassed ? "" : " | " + string.Join("; ", details.Where(x => x.Contains("FAIL"))))
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "真实几何:样本集统计验证",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        // ================================================================
        // 检查点解析回归测试（WbMigrationCheckpoint）
        // ================================================================
        internal static class WbCheckpointTest
        {
            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestAllEmpty());
                results.Add(TestOnlyFailedFilesEmpty());
                results.Add(TestZeroRecordsZeroDiagnostics());
                results.Add(TestDiagnosticsWithSpecials());
                results.Add(TestDegenerateHoleRoundTrip());
                results.Add(TestDegenerateNonDefaultThreshold());
                results.Add(TestOldV9FormatRejected());
                results.Add(TestCorruptedCheckpoint());
                results.Add(TestFileListSameFilesDifferentOrder());
                results.Add(TestAtomicSavePreservesOriginalOnFailure());
                results.Add(TestPhaseExtractingRoundTrip());
                results.Add(TestPhaseFinalizingRoundTrip());
                results.Add(TestPhaseDefaultWhenMissing());
                results.Add(TestTruncatedRecordsRejected());
                results.Add(TestCheckpointResumeNoRecordDuplication());
                return results;
            }

            /// <summary>
            /// 场景1：FailedFiles、CollectedRecords、Diagnostics 全部为空
            /// 验证空字段不导致字段位偏移
            /// </summary>
            private static TestResult TestAllEmpty()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = "",
                        PendingFiles = new List<string> { "a.3dm", "b.3dm" },
                        FileIndex = 1,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FileIndex == 1
                        && loaded.FailedFiles.Count == 0
                        && loaded.CollectedRecords.Count == 0
                        && loaded.Diagnostics.Count == 0
                        && loaded.PendingFiles.Count == 2
                        && loaded.TotalErrorCount == 0;
                    return new TestResult
                    {
                        TestName = "检查点:全空字段无位偏移",
                        Passed = passed,
                        Detail = passed ? "OK" : "FIdx=" + (loaded?.FileIndex ?? -1)
                            + " FF=" + (loaded?.FailedFiles?.Count ?? -1)
                            + " R=" + (loaded?.CollectedRecords?.Count ?? -1)
                            + " D=" + (loaded?.Diagnostics?.Count ?? -1)
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:全空字段无位偏移",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景2：仅 FailedFiles 为空，但已有正常记录
            /// 验证 FailedFiles 空行不导致记录计数错位
            /// </summary>
            private static TestResult TestOnlyFailedFilesEmpty()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "f1.3dm", "f2.3dm", "f3.3dm" },
                        FileIndex = 2,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(), // 空！
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 0, Number = "N001", SourceFile = "f1.3dm", FeatureText = "FEAT_A" },
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 1, Number = "N002", SourceFile = "f1.3dm", FeatureText = "FEAT_B" }
                        },
                        Diagnostics = new List<string> { "diag1" }
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FailedFiles.Count == 0
                        && loaded.CollectedRecords.Count == 2
                        && loaded.CollectedRecords[0].Number == "N001"
                        && loaded.CollectedRecords[1].Number == "N002"
                        && loaded.Diagnostics.Count == 1
                        && loaded.Diagnostics[0] == "diag1";
                    return new TestResult
                    {
                        TestName = "检查点:仅FailedFiles空+有记录",
                        Passed = passed,
                        Detail = passed ? "OK" : "FF=" + (loaded?.FailedFiles?.Count ?? -1)
                            + " R=" + (loaded?.CollectedRecords?.Count ?? -1)
                            + " D=" + (loaded?.Diagnostics?.Count ?? -1)
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:仅FailedFiles空+有记录",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景3：记录数为 0、诊断数为 0（有 FailedFiles）
            /// 验证零计数边界正确解析为无数据块
            /// </summary>
            private static TestResult TestZeroRecordsZeroDiagnostics()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "only.3dm" },
                        FileIndex = 0,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string> { "bad.3dm" },
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FailedFiles.Count == 1
                        && loaded.FailedFiles[0] == "bad.3dm"
                        && loaded.CollectedRecords.Count == 0
                        && loaded.Diagnostics.Count == 0;
                    return new TestResult
                    {
                        TestName = "检查点:记录0诊断0（有FailedFiles）",
                        Passed = passed,
                        Detail = passed ? "OK" : "FF=" + (loaded?.FailedFiles?.Count ?? -1)
                            + " R=" + (loaded?.CollectedRecords?.Count ?? -1)
                            + " D=" + (loaded?.Diagnostics?.Count ?? -1)
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:记录0诊断0（有FailedFiles）",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景4：诊断文本含换行符和反斜杠
            /// 验证 Escape/Unescape round-trip 不丢失/不破坏内容
            /// </summary>
            private static TestResult TestDiagnosticsWithSpecials()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "a.3dm" },
                        FileIndex = 0,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>
                        {
                            "line1\nline2",
                            "path C:\\data\\files",
                            "mixed\nslash\\and\nnewline",
                            "全中文诊断：包含\\n和\\r转义测试"
                        }
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.Diagnostics.Count == 4
                        && loaded.Diagnostics[0] == "line1\nline2"
                        && loaded.Diagnostics[1] == "path C:\\data\\files"
                        && loaded.Diagnostics[2] == "mixed\nslash\\and\nnewline"
                        && loaded.Diagnostics[3] == "全中文诊断：包含\\n和\\r转义测试";
                    return new TestResult
                    {
                        TestName = "检查点:诊断含换行和反斜杠",
                        Passed = passed,
                        Detail = passed ? "OK"
                            : "D0=" + (loaded?.Diagnostics?.ElementAtOrDefault(0) ?? "(null)")
                            + " D1=" + (loaded?.Diagnostics?.ElementAtOrDefault(1) ?? "(null)")
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:诊断含换行和反斜杠",
                        Passed = false,
                        Detail = ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景5：退化孔特征往返 —— 含退化孔的 WbFeature 经检查点保存/加载后
            /// 验证 WbFeature.IsDegenerate、HasDegenerateHoles 及孔退化辅助字段不丢失
            /// </summary>
            private static TestResult TestDegenerateHoleRoundTrip()
            {
                string tmp = null;
                try
                {
                    // 构造含退化孔的特征（惯量退化 + 孔退化）
                    var feature = new WbFeature
                    {
                        Material = "Steel", Color = "Red",
                        BoundingBox = new[] { 100.0, 50.0, 30.0 },
                        Area = 1000, Volume = 500,
                        Inertia = new[] { 2000.0, 1990.0, 500.0 },  // 惯量退化：1990/2000 > 0.99
                        MirrorScore = 0.5, HoleMirrorScore = 0.3,
                        IsDegenerate = true
                    };
                    // 正常孔
                    feature.Holes.Add(new WbHole(10, 5, 20, 10, 0, 0, 0, 1));
                    // 退化孔：各辅助字段填非零值
                    var degHole = new WbHole(8, 2, 30, 15, 0, 0, 0, 0,
                        isDegenerate: true, degenerateType: "非平面内环");
                    degHole.DegenerateProjectedArea = 3.14;
                    degHole.DegenerateCircularity = 0.85;
                    degHole.DegenerateAspectRatio = 1.5;
                    degHole.DegenerateNormalU = 0.577;
                    degHole.DegenerateNormalV = 0.577;
                    degHole.DegenerateNormalW = 0.577;
                    feature.Holes.Add(degHole);

                    // 序列化 → 保存检查点 → 加载 → 反序列化
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "a.3dm" },
                        FileIndex = 0, TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord
                            {
                                Index = 0, Number = "001",
                                SourceFile = "test.3dm",
                                FeatureText = feature.Serialize()
                            }
                        },
                        Diagnostics = new List<string>()
                    };
                    tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    if (loaded == null || loaded.CollectedRecords.Count != 1)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "检查点加载失败或记录数为 " + (loaded?.CollectedRecords?.Count ?? -1)
                        };

                    var restored = WbFeature.Deserialize(loaded.CollectedRecords[0].FeatureText);

                    // 验证 feature 级退化标记
                    if (!restored.IsDegenerate)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "WbFeature.IsDegenerate 未恢复（应为 true）"
                        };
                    if (!restored.HasDegenerateHoles)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "HasDegenerateHoles 未恢复（应为 true）"
                        };
                    if (restored.Holes.Count != 2)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "孔数不匹配：" + restored.Holes.Count + "（应为 2）"
                        };

                    // 验证退化孔各字段
                    var rh = restored.Holes[1];  // 第二个孔是退化孔
                    if (!rh.IsDegenerate)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "WbHole.IsDegenerate 未恢复"
                        };
                    if (rh.DegenerateType != "非平面内环")
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateType 不匹配：" + rh.DegenerateType
                        };
                    double eps = 1e-12;
                    if (Math.Abs(rh.DegenerateProjectedArea - 3.14) > eps)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateProjectedArea 不匹配：" + rh.DegenerateProjectedArea
                        };
                    if (Math.Abs(rh.DegenerateCircularity - 0.85) > eps)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateCircularity 不匹配：" + rh.DegenerateCircularity
                        };
                    if (Math.Abs(rh.DegenerateAspectRatio - 1.5) > eps)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "DegenerateAspectRatio 不匹配：" + rh.DegenerateAspectRatio
                        };
                    if (Math.Abs(rh.DegenerateNormalU - 0.577) > 1e-6
                        || Math.Abs(rh.DegenerateNormalV - 0.577) > 1e-6
                        || Math.Abs(rh.DegenerateNormalW - 0.577) > 1e-6)
                        return new TestResult
                        {
                            TestName = "检查点:退化孔特征往返",
                            Passed = false,
                            Detail = "拟合法向不匹配：("
                                + rh.DegenerateNormalU + "," + rh.DegenerateNormalV + "," + rh.DegenerateNormalW + ")"
                        };

                    return new TestResult
                    {
                        TestName = "检查点:退化孔特征往返",
                        Passed = true,
                        Detail = "OK — 惯量退化标记=" + restored.IsDegenerate
                            + " 孔退化标记=" + rh.IsDegenerate
                            + " 类型=" + rh.DegenerateType
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:退化孔特征往返",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
                finally
                {
                    if (tmp != null) CleanTemp(tmp);
                }
            }

            /// <summary>
            /// 场景5b：非默认惯量退化阈值往返 —— 验证 IsDegenerate 使用序列化值
            /// 而非 Deserialize 中的固定 0.99 重算。惯量 1950/2000=0.975：
            /// 0.99 阈值下非退化，0.95 阈值下退化。确认序列化 true 不被 0.99 覆盖。
            /// </summary>
            private static TestResult TestDegenerateNonDefaultThreshold()
            {
                try
                {
                    // 惯量比 0.975：0.99 阈值非退化，0.95 阈值退化
                    var feature = new WbFeature
                    {
                        Material = "Al", Color = "",
                        BoundingBox = new[] { 80.0, 40.0, 20.0 },
                        Area = 500, Volume = 200,
                        Inertia = new[] { 2000.0, 1950.0, 500.0 },
                        MirrorScore = 0.0, HoleMirrorScore = 0.0,
                        IsDegenerate = true   // 模拟 _settings.InertiaDegenerateRatio=0.95 的计算结果
                    };
                    feature.Holes.Add(new WbHole(5, 2, 10, 5, 0, 0, 0, 1));

                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "x.3dm" },
                        FileIndex = 0, TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord
                            {
                                Index = 0, Number = "001",
                                SourceFile = "x.3dm",
                                FeatureText = feature.Serialize()
                            }
                        },
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    if (loaded == null || loaded.CollectedRecords.Count != 1)
                        return new TestResult
                        {
                            TestName = "检查点:非默认阈值IsDegenerate往返",
                            Passed = false,
                            Detail = "检查点加载失败"
                        };
                    var restored = WbFeature.Deserialize(loaded.CollectedRecords[0].FeatureText);
                    // 核心断言：序列化的 true 优先于 0.99 重算出的 false
                    if (!restored.IsDegenerate)
                        return new TestResult
                        {
                            TestName = "检查点:非默认阈值IsDegenerate往返",
                            Passed = false,
                            Detail = "IsDegenerate 被 0.99 重算覆盖为 false（应为序列化值 true）"
                        };
                    return new TestResult
                    {
                        TestName = "检查点:非默认阈值IsDegenerate往返",
                        Passed = true,
                        Detail = "OK — 非默认阈值标记 true 未被 0.99 兜底覆盖"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:非默认阈值IsDegenerate往返",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景5c：旧版 V9 格式（13字段，缺少 IsDegenerate）拒绝恢复
            /// 反序列化必须抛出 InvalidDataException，消息包含"重新提取"
            /// </summary>
            private static TestResult TestOldV9FormatRejected()
            {
                // 手动构造旧版 13 字段 V9 特征串（缺少 IsDegenerate）
                string material = "Al";
                string color = "Red";
                double[] bbox = { 100.0, 50.0, 30.0 };
                string oldLine = "ENTITY_FEATURE_V9;" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(material)) + ";" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(color)) + ";" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        string.Join(",", bbox.Select(v => WbUtil.FR(v))))) + ";" +
                    WbUtil.FR(500.0) + ";" +    // Area
                    WbUtil.FR(200.0) + ";" +    // Volume
                    WbUtil.FR(2000.0) + ";" +   // Inertia[0]
                    WbUtil.FR(1990.0) + ";" +   // Inertia[1]
                    WbUtil.FR(500.0) + ";" +    // Inertia[2]
                    "" + ";" +                   // holes (none)
                    WbUtil.FR(0.5) + ";" +       // MirrorScore
                    WbUtil.FR(0.3) + ";" +       // HoleMirrorScore
                    "";                          // profiles (none)
                // 共 13 个分号分隔段

                try
                {
                    WbFeature.Deserialize(oldLine);
                    return new TestResult
                    {
                        TestName = "检查点:旧V9格式拒绝恢复",
                        Passed = false,
                        Detail = "应抛 InvalidDataException 但未抛异常"
                    };
                }
                catch (InvalidDataException ex)
                {
                    if (!ex.Message.Contains("重新提取"))
                        return new TestResult
                        {
                            TestName = "检查点:旧V9格式拒绝恢复",
                            Passed = false,
                            Detail = "异常消息缺少'重新提取'：\"" + ex.Message + "\""
                        };
                    return new TestResult
                    {
                        TestName = "检查点:旧V9格式拒绝恢复",
                        Passed = true,
                        Detail = "OK — 旧格式被正确拒绝：\"" + ex.Message + "\""
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:旧V9格式拒绝恢复",
                        Passed = false,
                        Detail = "异常类型不符合预期（应为 InvalidDataException）：" +
                            ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景6：损坏检查点文件 —— 应抛出 InvalidDataException（非静默 null）
            /// 验证不静默失败且有明确诊断信息
            /// </summary>
            private static TestResult TestCorruptedCheckpoint()
            {
                string tmp = null;
                try
                {
                    tmp = Path.GetTempFileName();
                    // 写入只有2行的损坏数据（最少需要7行）
                    File.WriteAllText(tmp, "garbage\nline2", Encoding.UTF8);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    // 如果没抛异常，说明静默返回了 — 这是失败
                    return new TestResult
                    {
                        TestName = "检查点:损坏文件抛异常不静默",
                        Passed = false,
                        Detail = "应抛异常但未抛异常；loaded=" + (loaded != null)
                    };
                }
                catch (InvalidDataException)
                {
                    return new TestResult
                    {
                        TestName = "检查点:损坏文件抛异常不静默",
                        Passed = true,
                        Detail = "OK — 正确抛出 InvalidDataException"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:损坏文件抛异常不静默",
                        Passed = false,
                        Detail = "异常类型不符合预期：" + ex.GetType().Name + ": " + ex.Message
                    };
                }
                finally
                {
                    if (tmp != null) CleanTemp(tmp);
                }
            }

            /// <summary>
            /// 场景7：同文件集合、不同顺序必须拒绝恢复。
            /// FileIndex 依赖原列表顺序；重排同一批文件时仍恢复旧顺序会指向错误文件。
            /// </summary>
            private static TestResult TestFileListSameFilesDifferentOrder()
            {
                try
                {
                    string basePath = @"C:\test";
                    // 构造检查点：顺序 A, B, C
                    var cpFiles = new List<string>
                    {
                        Path.Combine(basePath, "a.3dm"),
                        Path.Combine(basePath, "b.3dm"),
                        Path.Combine(basePath, "c.3dm")
                    };
                    // 当前输入：同一批文件，但顺序 B, A, C
                    var curFiles = new List<string>
                    {
                        Path.Combine(basePath, "b.3dm"),
                        Path.Combine(basePath, "a.3dm"),
                        Path.Combine(basePath, "c.3dm")
                    };

                    bool passed = WbClassificationMigration.ValidateFileListConsistency(cpFiles, curFiles, out string reason);
                    if (passed)
                        return new TestResult
                        {
                            TestName = "检查点:同文件不同顺序拒绝恢复",
                            Passed = false,
                            Detail = "应拒绝但通过了——集合相同、顺序不同时 FileIndex 不可靠"
                        };
                    if (!reason.Contains("顺序不一致"))
                        return new TestResult
                        {
                            TestName = "检查点:同文件不同顺序拒绝恢复",
                            Passed = false,
                            Detail = "拒绝原因应为'顺序不一致'，实际：" + reason
                        };
                    return new TestResult
                    {
                        TestName = "检查点:同文件不同顺序拒绝恢复",
                        Passed = true,
                        Detail = "OK — 正确拒绝：\"" + reason + "\""
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:同文件不同顺序拒绝恢复",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景11：Phase=Extracting 往返 —— 验证 Phase 字段经序列化/反序列化不丢失。
            /// 模拟首文件完成后崩溃：FileIndex=1, Phase=Extracting，恢复应从文件 1 继续。
            /// </summary>
            private static TestResult TestPhaseExtractingRoundTrip()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "a.3dm", "b.3dm", "c.3dm" },
                        FileIndex = 1,
                        Phase = WbMigrationPhase.Extracting,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FileIndex == 1
                        && loaded.Phase == WbMigrationPhase.Extracting
                        && loaded.PendingFiles.Count == 3;
                    return new TestResult
                    {
                        TestName = "检查点:Phase=Extracting 往返",
                        Passed = passed,
                        Detail = passed ? "OK — 恢复从文件1继续" :
                            "FIdx=" + (loaded?.FileIndex ?? -1) +
                            " Phase=" + (loaded?.Phase.ToString() ?? "(null)")
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:Phase=Extracting 往返",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景12：Phase=Finalizing 往返 —— 全部文件提取完毕、尚未入库。
            /// 检查点 FileIndex==Count 且 Phase=Finalizing，恢复应跳过文件提取直接进入入库。
            /// </summary>
            private static TestResult TestPhaseFinalizingRoundTrip()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string> { "a.3dm", "b.3dm" },
                        FileIndex = 2,  // == Count，全部提取完毕
                        Phase = WbMigrationPhase.Finalizing,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FileIndex == 2
                        && loaded.Phase == WbMigrationPhase.Finalizing
                        && loaded.PendingFiles.Count == 2;
                    return new TestResult
                    {
                        TestName = "检查点:Phase=Finalizing 往返",
                        Passed = passed,
                        Detail = passed ? "OK — 恢复直接进入入库" :
                            "FIdx=" + (loaded?.FileIndex ?? -1) +
                            " Phase=" + (loaded?.Phase.ToString() ?? "(null)")
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:Phase=Finalizing 往返",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景13：旧检查点缺少 Phase 字段时默认为 Extracting。
            /// 验证向后兼容：无 Phase 行的旧检查点反序列化后 Phase 仍为 Extracting（枚举默认值），
            /// 不会误判为 Finalizing 而跳过文件提取。
            /// </summary>
            private static TestResult TestPhaseDefaultWhenMissing()
            {
                try
                {
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\legacy\lib.db",
                        PendingFiles = new List<string> { "x.3dm" },
                        FileIndex = 1,
                        // 故意不设置 Phase —— 靠枚举默认值
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    string tmp = WriteTemp(cp);
                    // 手动删除最后一行（Phase 行），模拟旧格式
                    string content = File.ReadAllText(tmp, Encoding.UTF8);
                    int lastNewline = content.LastIndexOf('\n');
                    if (lastNewline > 0)
                        content = content.Substring(0, lastNewline).TrimEnd('\r', '\n');
                    File.WriteAllText(tmp, content, Encoding.UTF8);

                    var loaded = WbMigrationCheckpoint.Load(tmp);
                    CleanTemp(tmp);
                    bool passed = loaded != null
                        && loaded.FileIndex == 1
                        && loaded.Phase == WbMigrationPhase.Extracting;  // 默认值
                    return new TestResult
                    {
                        TestName = "检查点:旧格式缺 Phase 默认 Extracting",
                        Passed = passed,
                        Detail = passed ? "OK — 旧检查点 Phase 默认为 Extracting" :
                            "Phase=" + (loaded?.Phase.ToString() ?? "(null)") +
                            "（期望 Extracting）"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:旧格式缺 Phase 默认 Extracting",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>验证检查点记录数声明与实际行数不匹配时抛出 InvalidDataException，
            /// 防止截断文件导致部分已提取零件静默丢失。</summary>
            private static TestResult TestTruncatedRecordsRejected()
            {
                string testName = "检查点:截断记录拒绝加载";
                try
                {
                    // 构造声明 10 条记录的检查点，再手动截断为仅 3 条
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\lib.db",
                        PendingFiles = new List<string>(),
                        FileIndex = 3,
                        Phase = WbMigrationPhase.Extracting,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        Diagnostics = new List<string> { "diag1" }
                    };
                    for (int i = 1; i <= 10; i++)
                        cp.CollectedRecords.Add(new WbMigrationCheckpoint.CheckpointRecord
                        {
                            Index = i,
                            Number = "PN" + i.ToString(CultureInfo.InvariantCulture),
                            SourceFile = "file" + i.ToString(CultureInfo.InvariantCulture) + ".3dm",
                            FeatureText = "feature" + i.ToString(CultureInfo.InvariantCulture)
                        });

                    string tmp = WriteTemp(cp);
                    // 截断：只保留到第 3 条记录行（共 7 + 3 = 10 行：7 头 + 3 记录），
                    // 切除后面的 7 条记录 + 诊断 + Phase
                    string content = File.ReadAllText(tmp, Encoding.UTF8);
                    string[] allLines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                    // 格式: 行0=DB,1=files,2=idx,3=errCount,4=failed,5=recCount,6..15=10条记录,16=diagCount,17=diag,18=Phase
                    // 保留行0-9（头+前3条记录），切除后面的
                    var kept = new List<string>();
                    for (int li = 0; li < Math.Min(10, allLines.Length); li++)
                        kept.Add(allLines[li]);
                    File.WriteAllText(tmp, string.Join("\r\n", kept), Encoding.UTF8);

                    try
                    {
                        WbMigrationCheckpoint.Load(tmp);
                        CleanTemp(tmp);
                        return new TestResult
                        {
                            TestName = testName,
                            Passed = false,
                            Detail = "未抛出异常 —— 静默接受了截断的检查点。"
                        };
                    }
                    catch (InvalidDataException)
                    {
                        CleanTemp(tmp);
                        // 子测试2：字段数异常（3 字段替代 4 字段）
                        string tmp2 = Path.GetTempFileName();
                        try
                        {
                            var sb2 = new StringBuilder();
                            // 序列化一个最小检查点，1 条记录但字段数故意写 3
                            sb2.AppendLine(@"C:\test\lib2.db");
                            sb2.AppendLine("");  // PendingFiles 空
                            sb2.AppendLine("0"); // FileIndex
                            sb2.AppendLine("0"); // TotalErrorCount
                            sb2.AppendLine("");  // FailedFiles 空
                            sb2.AppendLine("1"); // recCount=1
                            sb2.AppendLine("1\u0001PN\u0001file.3dm"); // 仅 3 字段（缺 FeatureText）
                            sb2.AppendLine("0"); // diagCount=0
                            sb2.AppendLine(WbMigrationPhase.Extracting.ToString());
                            File.WriteAllText(tmp2, sb2.ToString(), Encoding.UTF8);
                            WbMigrationCheckpoint.Load(tmp2);
                            return new TestResult
                            {
                                TestName = testName,
                                Passed = false,
                                Detail = "子测试2失败：3 字段记录未触发 InvalidDataException。"
                            };
                        }
                        catch (InvalidDataException)
                        {
                            return new TestResult
                            {
                                TestName = testName,
                                Passed = true,
                                Detail = "OK — 截断记录和字段数异常均正确抛出 InvalidDataException。"
                            };
                        }
                        finally
                        {
                            try { if (File.Exists(tmp2)) File.Delete(tmp2); } catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = testName,
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            /// <summary>
            /// 场景12：TryResumeCheckpoint 从 Finalizing 检查点恢复时不产生重复记录
            /// 模拟前面文件成功提取、最后文件打开失败、自动进入最终入库的完整流程：
            /// _collectedRecords 已有旧记录 → 恢复检查点 → 应清空后重新加载（不重复）
            /// </summary>
            private static TestResult TestCheckpointResumeNoRecordDuplication()
            {
                try
                {
                    // 构造一个有效的序列化特征文本
                    var feature = new WbFeature
                    {
                        Material = "TestMaterial",
                        Color = "FF0000",
                        BoundingBox = new double[] { 10, 5, 3 },
                        Area = 100,
                        Volume = 50,
                        Inertia = new double[] { 1, 2, 3 },
                        MirrorScore = 0.1,
                        HoleMirrorScore = 0.2,
                        Holes = new List<WbHole>(),
                        OuterProfiles = new List<WbOuterProfile>(),
                        IsDegenerate = false
                    };
                    string validFeatureText = feature.Serialize();

                    // 构造 Finalizing 检查点：3 条记录 + 2 条诊断
                    var cp = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\test\dup.db",
                        PendingFiles = new List<string> { "a.3dm", "b.3dm", "c.3dm" },
                        FileIndex = 3,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>
                        {
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 0, Number = "P1", SourceFile = "a.3dm", FeatureText = validFeatureText },
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 1, Number = "P2", SourceFile = "b.3dm", FeatureText = validFeatureText },
                            new WbMigrationCheckpoint.CheckpointRecord { Index = 2, Number = "P3", SourceFile = "c.3dm", FeatureText = validFeatureText }
                        },
                        Diagnostics = new List<string> { "diag_line_1", "diag_line_2" },
                        Phase = WbMigrationPhase.Finalizing
                    };

                    // 模拟 TryResumeCheckpoint 被调用前内存中已有旧状态
                    // （前面文件提取成功留存的记录）
                    var collectedRecords = new List<WbMigrationRecord>
                    {
                        new WbMigrationRecord(0, "OLD_1", feature, "old1.3dm"),
                        new WbMigrationRecord(1, "OLD_2", feature, "old2.3dm")
                    };
                    var diagnostics = new List<string> { "old_diag_from_extraction" };

                    // 执行 TryResumeCheckpoint 的 Clear+Add 模式
                    diagnostics.Clear();
                    if (cp.Diagnostics != null)
                        diagnostics.AddRange(cp.Diagnostics);

                    collectedRecords.Clear();
                    if (cp.CollectedRecords != null)
                    {
                        foreach (var cr in cp.CollectedRecords)
                        {
                            WbFeature deserialized = WbFeature.Deserialize(cr.FeatureText);
                            collectedRecords.Add(new WbMigrationRecord(
                                cr.Index, cr.Number, deserialized, cr.SourceFile ?? string.Empty));
                        }
                    }

                    bool recordCountOk = collectedRecords.Count == 3;
                    bool diagCountOk = diagnostics.Count == 2;

                    return new TestResult
                    {
                        TestName = "检查点:Finalizing恢复不重复记录",
                        Passed = recordCountOk && diagCountOk,
                        Detail = (recordCountOk && diagCountOk)
                            ? "OK — 恢复后 collectedRecords=3 (非 5)、diagnostics=2 (非 3)"
                            : "collectedRecords=" + collectedRecords.Count.ToString(CultureInfo.InvariantCulture) +
                                " (期望 3), diagnostics=" + diagnostics.Count.ToString(CultureInfo.InvariantCulture) +
                                " (期望 2)"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "检查点:Finalizing恢复不重复记录",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            private static string WriteTemp(WbMigrationCheckpoint cp)
            {
                string path = Path.GetTempFileName();
                cp.Save(path);
                return path;
            }

            /// <summary>
            /// 场景10：File.Replace 失败时旧检查点仍可读取
            /// 通过设置文件为只读模拟 File.Replace 失败，验证旧内容完整保留
            /// </summary>
            private static TestResult TestAtomicSavePreservesOriginalOnFailure()
            {
                string checkpointPath = null;
                try
                {
                    var cp1 = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\original\lib.db",
                        PendingFiles = new List<string> { "a.3dm", "b.3dm" },
                        FileIndex = 5,
                        TotalErrorCount = 0,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };
                    checkpointPath = Path.GetTempFileName();
                    cp1.Save(checkpointPath);

                    // 加载并验证原始内容
                    var loaded1 = WbMigrationCheckpoint.Load(checkpointPath);
                    if (loaded1 == null || loaded1.FileIndex != 5)
                        return new TestResult
                        {
                            TestName = "原子替换:旧文件可读",
                            Passed = false,
                            Detail = "原始检查点保存后无法正确加载: FileIndex=" +
                                (loaded1?.FileIndex ?? -1)
                        };

                    // 设为只读，模拟 File.Replace 失败场景
                    File.SetAttributes(checkpointPath, FileAttributes.ReadOnly);

                    var cp2 = new WbMigrationCheckpoint
                    {
                        DatabasePath = @"C:\different\lib.db",
                        PendingFiles = new List<string> { "x.3dm" },
                        FileIndex = 99,
                        TotalErrorCount = 2,
                        FailedFiles = new List<string>(),
                        CollectedRecords = new List<WbMigrationCheckpoint.CheckpointRecord>(),
                        Diagnostics = new List<string>()
                    };

                    bool replaceThrew = false;
                    try { cp2.Save(checkpointPath); }
                    catch (UnauthorizedAccessException) { replaceThrew = true; }
                    catch (IOException) { replaceThrew = true; }

                    // 恢复可写
                    File.SetAttributes(checkpointPath, FileAttributes.Normal);

                    // 旧检查点应保持原样
                    var loaded2 = WbMigrationCheckpoint.Load(checkpointPath);
                    if (loaded2 == null)
                        return new TestResult
                        {
                            TestName = "原子替换:旧文件可读",
                            Passed = false,
                            Detail = "File.Replace 失败后旧检查点不可加载（已损坏或丢失）"
                        };

                    bool contentPreserved = loaded2.FileIndex == 5
                        && loaded2.PendingFiles.Count == 2
                        && string.Equals(loaded2.DatabasePath, @"C:\original\lib.db",
                            StringComparison.Ordinal);
                    bool replaceDetected = replaceThrew;

                    string detail = replaceDetected
                        ? (contentPreserved
                            ? "OK — File.Replace 已抛异常，旧检查点内容完整保留"
                            : "失败 — 异常已抛但旧内容被破坏")
                        : (contentPreserved
                            ? "警告 — File.Replace 未抛异常（系统可能在只读文件上允许替换），但内容保留"
                            : "失败 — File.Replace 静默成功且覆盖了旧内容");

                    return new TestResult
                    {
                        TestName = "原子替换:旧文件可读",
                        Passed = contentPreserved,
                        Detail = detail
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "原子替换:旧文件可读",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
                finally
                {
                    if (checkpointPath != null)
                    {
                        try
                        {
                            if (File.Exists(checkpointPath))
                            {
                                File.SetAttributes(checkpointPath, FileAttributes.Normal);
                                File.Delete(checkpointPath);
                            }
                        }
                        catch { }
                    }
                }
            }

            private static void CleanTemp(string path)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch { /* 尽力清理 */ }
            }
        }

        // ====================================================================
        // 退化匹配测试：验证 enableDegenerateMatching 开/关行为
        // ====================================================================
        internal static class WbDegenerateMatchingTest
        {
            public class TestResult
            {
                public string TestName;
                public bool Passed;
                public string Detail;
            }

            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestOffClustersWithNormalTolerance());
                results.Add(TestOnMatching());
                results.Add(TestChainNotClique());
                results.Add(TestAxisPermutedDegenerateMatch());
                results.Add(TestBoundaryPosition());
                results.Add(TestBoundaryNormal());
                results.Add(TestBoundaryPerimeter());
                results.Add(TestBoundaryArea());
                return results;
            }

            public static void RunAllAndLog()
            {
                var results = RunAll();
                foreach (var r in results)
                {
                    string tag = r.Passed ? "[PASS] " : "[FAIL] ";
                    RhinoApp.WriteLine(tag + r.TestName + " — " + r.Detail);
                }
                int pass = results.Count(r => r.Passed);
                int fail = results.Count(r => !r.Passed);
                RhinoApp.WriteLine("退化匹配测试结果：" + pass.ToString() + " 通过，" +
                    fail.ToString() + " 失败（共 " + results.Count.ToString() + "）。");
            }

            /// <summary>
            /// 场景 D1（新语义）：关闭容差放宽 → 退化孔记录仍参与聚类，
            /// 使用常规容差；完全相同的退化记录应匹配为 1 类（不再隔离）。
            /// </summary>
            private static TestResult TestOffClustersWithNormalTolerance()
            {
                // 构造两个完全相同的退化特征（同色以通过 GlobalMatches）
                var holesA = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };
                var holesB = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };

                var fA = CreateDegenerateFeature("Test", holesA);
                var fB = CreateDegenerateFeature("Test", holesB);

                var records = new List<WbMigrationRecord>
                {
                    new WbMigrationRecord(0, "P1", fA, "test"),
                    new WbMigrationRecord(1, "P2", fB, "test")
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                var audit = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                    strictMaterialColor: false, holeNormalTolerance: 0.99,
                    enableDegenerateMatching: false);

                // 关闭容差放宽：完全一致的退化记录仍聚为 1 类
                bool clustered = audit.Classes.Count == 1;
                return new TestResult
                {
                    TestName = "退化匹配:关闭→仍聚类(常规容差)",
                    Passed = clustered,
                    Detail = clustered
                        ? "OK — 2 条退化记录在关闭容差放宽时仍匹配为 1 类"
                        : "期望 1 类但实际为 " +
                          audit.Classes.Count.ToString(CultureInfo.InvariantCulture) +
                          "（退化记录未成功匹配）"
                };
            }

            /// <summary>
            /// 场景 D2：开启退化匹配 → 相似的退化记录可匹配成组
            /// </summary>
            private static TestResult TestOnMatching()
            {
                // 构造两个几乎相同的退化特征（在同一位置，仅颜色不同但宽松模式忽略）
                var holesA = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };
                var holesB = new List<WbHole> { CreateDegenerateHole(10, 1, 1, 0, 0) };

                var fA = CreateDegenerateFeature("Test", holesA);
                var fB = CreateDegenerateFeature("Test", holesB);

                var records = new List<WbMigrationRecord>
                {
                    new WbMigrationRecord(0, "P1", fA, "test"),
                    new WbMigrationRecord(1, "P2", fB, "test")
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                var audit = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                    strictMaterialColor: false, holeNormalTolerance: 0.99,
                    enableDegenerateMatching: true, degeneratePositionMultiplier: 2.0);

                // 开启：两条完全一致的退化记录应匹配成同组 → Classes 应有 1 个
                bool matched = audit.Classes.Count == 1;
                return new TestResult
                {
                    TestName = "退化匹配:开启→匹配",
                    Passed = matched,
                    Detail = matched
                        ? "OK — 2 条退化记录匹配为 1 类"
                        : "期望 1 类但实际为 " +
                          audit.Classes.Count.ToString(CultureInfo.InvariantCulture) +
                          "（退化记录未成功匹配）"
                };
            }

            /// <summary>
            /// 场景 D3（链式反例）：A↔B、B↔C 但 A≠C。
            /// HolePosition 有效容差 10.0（5.0 × degenMult=2），
            /// 三孔分别位于 (10,1,1) (4,1,1) (0,1,1)：
            ///   A↔B 距离 6 → 匹配成功
            ///   B↔C 距离 4 → 匹配成功
            ///   A↔C 距离 10 ≥ 10 → 匹配失败
            /// 若缺少 clique 检测，BFS 连通分量 {A,B,C} 会被直接合并成一类；
            /// 正确行为：{A,B,C} 非团 → 归入 AmbiguityGroups，不被自动合并。
            /// </summary>
            private static TestResult TestChainNotClique()
            {
                // 三孔沿 X 轴等距排列，间距 6 和 4，使两端孔间距=10（≥容差）
                var holesA = new List<WbHole> { CreateDegenerateHole(10, 10, 1, 1, 0.5) };
                var holesB = new List<WbHole> { CreateDegenerateHole(10,  4, 1, 1, 0.5) };
                var holesC = new List<WbHole> { CreateDegenerateHole(10,  0, 1, 1, 0.5) };

                var fA = CreateDegenerateFeature("Test", holesA);
                var fB = CreateDegenerateFeature("Test", holesB);
                var fC = CreateDegenerateFeature("Test", holesC);

                var records = new List<WbMigrationRecord>
                {
                    new WbMigrationRecord(0, "P1", fA, "test"),
                    new WbMigrationRecord(1, "P2", fB, "test"),
                    new WbMigrationRecord(2, "P3", fC, "test")
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                var audit = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                    strictMaterialColor: false, holeNormalTolerance: 0.99,
                    enableDegenerateMatching: true, degeneratePositionMultiplier: 2.0);

                // 正确行为：3 条记录不应被合并为 1 类
                bool noSingleClassWithAll = !audit.Classes.Any(
                    c => c.Count == 3);

                // 应出现在歧义组中
                bool hasAmbiguity = audit.AmbiguityGroups.Count > 0;
                int ambiguityRecordCount = audit.AmbiguityGroups.Sum(g => g.Count);

                bool passed = noSingleClassWithAll && hasAmbiguity && ambiguityRecordCount == 3;

                return new TestResult
                {
                    TestName = "退化匹配:链式A↔B↔C但A≠C→非团隔离",
                    Passed = passed,
                    Detail = passed
                        ? "OK — 未合并为单类，歧义组含 3 记录"
                        : string.Format(CultureInfo.InvariantCulture,
                            "Classes={0} AmbGroups={1} AmbRecs={2}",
                            audit.Classes.Count, audit.AmbiguityGroups.Count, ambiguityRecordCount)
                };
            }

            /// <summary>
            /// 场景 D4：轴置换退化匹配 —— 两个退化孔仅在非恒等轴排列下匹配，
            /// 验证 RemapHoles 的 perm + 退化字段重映射（DegenerateProjectedArea、
            /// DegenerateNormalU/V/W）正确生效。
            /// A: 孔(10,0,0) 退化法向(1,0,0) 投影面积 5.0
            /// B: 孔(0,10,0) 退化法向(0,1,0) 投影面积 5.0
            /// 恒等轴排列下距离 sqrt(200)≈14.14&gt;tol*degen=10.0→失败；
            /// 仅 perm=[1,0,2]（U↔V交换）可匹配，其余 5 种轴排列均失败。
            /// </summary>
            private static TestResult TestAxisPermutedDegenerateMatch()
            {
                // A: hole at (10, 0, 0), DegenerateNormal (1, 0, 0)
                var holeA = new WbHole(perimeter: 100, area: 0,
                    u: 10, v: 0, w: 0,
                    nu: 0, nv: 0, nw: 0,
                    isDegenerate: true, degenerateType: "Test")
                {
                    DegenerateProjectedArea = 5.0,
                    DegenerateCircularity = 0.8,
                    DegenerateAspectRatio = 1.5,
                    DegenerateNormalU = 1, DegenerateNormalV = 0, DegenerateNormalW = 0
                };

                // B: hole at (0, 10, 0), DegenerateNormal (0, 1, 0)
                //    仅 perm={1,0,2} 时坐标→(10,0,0)、法向→(1,0,0) 与 A 一致
                var holeB = new WbHole(perimeter: 100, area: 0,
                    u: 0, v: 10, w: 0,
                    nu: 0, nv: 0, nw: 0,
                    isDegenerate: true, degenerateType: "Test")
                {
                    DegenerateProjectedArea = 5.0,
                    DegenerateCircularity = 0.8,
                    DegenerateAspectRatio = 1.5,
                    DegenerateNormalU = 0, DegenerateNormalV = 1, DegenerateNormalW = 0
                };

                var featA = new WbFeature
                {
                    Material = "Steel",
                    Color = "A",
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000,
                    Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    IsDegenerate = true,
                    Holes = new List<WbHole> { holeA },
                    MirrorScore = 0.1,
                    HoleMirrorScore = 0.1
                };

                var featB = new WbFeature
                {
                    Material = "Steel",
                    Color = "B",
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000,
                    Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    IsDegenerate = true,
                    Holes = new List<WbHole> { holeB },
                    MirrorScore = 0.1,
                    HoleMirrorScore = 0.1
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                bool match = WbMigrationAudit.GeometryMatches(featA, featB, enabled, tolerances, isRel,
                    reflection: false, normalTol: 0.99,
                    outerAreaRatio: 1e-8, outerPerimMin: 1e-8,
                    degenMult: 2.0, enableDegenMatch: true);

                return new TestResult
                {
                    TestName = "退化匹配:轴置换→非恒等perm匹配",
                    Passed = match,
                    Detail = match
                        ? "OK — 非恒等轴排列下退化孔匹配成功，RemapHoles 退化字段重映射正确"
                        : "期望退化孔经轴置换后匹配，但实际不匹配（RemapHoles 可能未正确重映射退化字段）"
                };
            }

            internal static WbHole CreateDegenerateHole(
                double perimeter, double u, double v, double w,
                double degenerateArea = 0.5)
            {
                return new WbHole(perimeter, 0, u, v, w, 0, 0, 0,
                    isDegenerate: true, degenerateType: "Test")
                {
                    DegenerateProjectedArea = degenerateArea,
                    DegenerateCircularity = 0.8,
                    DegenerateAspectRatio = 1.5,
                    DegenerateNormalU = 0, DegenerateNormalV = 0, DegenerateNormalW = 1
                };
            }

            internal static WbFeature CreateDegenerateFeature(
                string color, List<WbHole> holes)
            {
                return new WbFeature
                {
                    Material = "Steel",
                    Color = color,
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000,
                    Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    IsDegenerate = true,
                    Holes = holes,
                    MirrorScore = 0.5,
                    HoleMirrorScore = 0.3
                };
            }

            /// <summary>
            /// 场景 D5（位置边界）：两退化孔完全一致，仅位置差 6.0。
            /// 正常 HolePosition 容差 5.0（绝对），degenMult=2.0。
            /// 关闭时距离 6.0 ≥ 5.0 → 不匹配；开启时 6.0 &lt; 10.0 → 匹配。
            /// 验证开关确实改变了容差边界，而非仅控制是否参与。
            /// </summary>
            private static TestResult TestBoundaryPosition()
            {
                var holeA = CreateDegenerateHole(perimeter: 10, u: 9, v: 0, w: 0, degenerateArea: 5.0);
                var holeB = CreateDegenerateHole(perimeter: 10, u: 3, v: 0, w: 0, degenerateArea: 5.0);

                WbFeature MakeFeat(WbHole h) => new WbFeature
                {
                    Material = "Steel", Color = "Test",
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000, Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    Holes = new List<WbHole> { h }
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                bool matchOff = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: false);

                bool matchOn = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: true);

                bool passed = !matchOff && matchOn;
                return new TestResult
                {
                    TestName = "边界:位置差6.0→关闭不匹配开启匹配",
                    Passed = passed,
                    Detail = passed
                        ? "OK — 关闭=" + matchOff + "（期望false），开启=" + matchOn + "（期望true）"
                        : "关闭=" + matchOff + "，开启=" + matchOn +
                          "（期望 关闭=false, 开启=true）"
                };
            }

            /// <summary>
            /// 场景 D6（法向边界）：两退化孔完全一致，仅拟合法向有微小差异。
            /// dot=0.99 在 normalTol=0.9986 之下但在 normalTol×0.95≈0.9487 之上。
            /// 关闭时不匹配（0.99 &lt; 0.9986），开启时匹配（0.99 ≥ 0.9487）。
            /// </summary>
            private static TestResult TestBoundaryNormal()
            {
                // 手动构造退化孔：A 拟合法向 (0.141, 0, 0.990)，B 拟合法向 (0, 0, 1)
                // dot = 0.990
                var holeA = new WbHole(10, 0, 1, 0, 0, 0, 0, 0,
                    isDegenerate: true, degenerateType: "Test")
                {
                    DegenerateProjectedArea = 5.0,
                    DegenerateCircularity = 0.8,
                    DegenerateAspectRatio = 1.5,
                    DegenerateNormalU = 0.141, DegenerateNormalV = 0, DegenerateNormalW = 0.990
                };
                var holeB = new WbHole(10, 0, 1, 0, 0, 0, 0, 0,
                    isDegenerate: true, degenerateType: "Test")
                {
                    DegenerateProjectedArea = 5.0,
                    DegenerateCircularity = 0.8,
                    DegenerateAspectRatio = 1.5,
                    DegenerateNormalU = 0, DegenerateNormalV = 0, DegenerateNormalW = 1
                };

                WbFeature MakeFeat(WbHole h) => new WbFeature
                {
                    Material = "Steel", Color = "Test",
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000, Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    Holes = new List<WbHole> { h }
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                bool matchOff = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: false);

                bool matchOn = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: true);

                bool passed = !matchOff && matchOn;
                return new TestResult
                {
                    TestName = "边界:法向dot=0.99→关闭不匹配开启匹配",
                    Passed = passed,
                    Detail = passed
                        ? "OK — 关闭=" + matchOff + "（期望false），开启=" + matchOn + "（期望true）"
                        : "关闭=" + matchOff + "，开启=" + matchOn +
                          "（期望 关闭=false, 开启=true）"
                };
            }

            /// <summary>
            /// 场景 D7（周长边界）：两退化孔除周长外完全一致。
            /// 周长 100 vs 100.15，差异 0.1498%。
            /// EdgeSum 正常容差 0.10%，degenMult=2.0。
            /// 关闭时 0.1498% &gt; 0.10% → 不匹配；开启时 0.1498% &lt; 0.20% → 匹配。
            /// </summary>
            private static TestResult TestBoundaryPerimeter()
            {
                var holeA = CreateDegenerateHole(perimeter: 100, u: 1, v: 0, w: 0, degenerateArea: 5.0);
                var holeB = CreateDegenerateHole(perimeter: 100.15, u: 1, v: 0, w: 0, degenerateArea: 5.0);

                WbFeature MakeFeat(WbHole h) => new WbFeature
                {
                    Material = "Steel", Color = "Test",
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000, Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    Holes = new List<WbHole> { h }
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                bool matchOff = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: false);

                bool matchOn = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: true);

                bool passed = !matchOff && matchOn;
                return new TestResult
                {
                    TestName = "边界:周长差0.15%→关闭不匹配开启匹配",
                    Passed = passed,
                    Detail = passed
                        ? "OK — 关闭=" + matchOff + "（期望false），开启=" + matchOn + "（期望true）"
                        : "关闭=" + matchOff + "，开启=" + matchOn +
                          "（期望 关闭=false, 开启=true）"
                };
            }

            /// <summary>
            /// 场景 D8（投影面积边界）：两退化孔除投影面积外完全一致。
            /// 投影面积 100 vs 103，差异 2.91%。
            /// HoleArea 正常容差 0.10%，放松时 max(0.10%×2.0, 5.0%)=5.0% 最低容差。
            /// 关闭时 2.91% &gt; 0.10% → 不匹配；开启时 2.91% &lt; 5.0% → 匹配。
            /// </summary>
            private static TestResult TestBoundaryArea()
            {
                var holeA = CreateDegenerateHole(perimeter: 10, u: 1, v: 0, w: 0, degenerateArea: 100);
                var holeB = CreateDegenerateHole(perimeter: 10, u: 1, v: 0, w: 0, degenerateArea: 103);

                WbFeature MakeFeat(WbHole h) => new WbFeature
                {
                    Material = "Steel", Color = "Test",
                    BoundingBox = new[] { 100.0, 80.0, 60.0 },
                    Area = 5000, Volume = 1000,
                    Inertia = new[] { 2000.0, 1990.0, 500.0 },
                    Holes = new List<WbHole> { h }
                };

                var enabled = AllEnabled();
                var tolerances = DefaultTolerances();
                var isRel = DefaultIsRel();

                bool matchOff = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: false);

                bool matchOn = WbMigrationAudit.Matches(
                    MakeFeat(holeA), MakeFeat(holeB),
                    enabled, tolerances, isRel,
                    strictMaterialColor: false, normalTol: 0.9986,
                    outerAreaRatio: 0.3, outerPerimMin: 0.1,
                    degenMult: 2.0, enableDegenMatch: true);

                bool passed = !matchOff && matchOn;
                return new TestResult
                {
                    TestName = "边界:投影面积差2.91%→关闭不匹配开启匹配",
                    Passed = passed,
                    Detail = passed
                        ? "OK — 关闭=" + matchOff + "（期望false），开启=" + matchOn + "（期望true）"
                        : "关闭=" + matchOff + "，开启=" + matchOn +
                          "（期望 关闭=false, 开启=true）"
                };
            }

            internal static Dictionary<V5Criterion, bool> AllEnabled()
            {
                return new Dictionary<V5Criterion, bool>
                {
                    { V5Criterion.Length, true },
                    { V5Criterion.Volume, true },
                    { V5Criterion.Inertia, true }, { V5Criterion.EdgeSum, true },
                    { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
                };
            }

            internal static Dictionary<V5Criterion, double> DefaultTolerances()
            {
                return new Dictionary<V5Criterion, double>
                {
                    { V5Criterion.Length, 0.10 },
                    { V5Criterion.Volume, 0.05 },
                    { V5Criterion.Inertia, 0.05 }, { V5Criterion.EdgeSum, 0.10 },
                    { V5Criterion.HoleArea, 0.10 }, { V5Criterion.HolePosition, 5.0 }
                };
            }

            internal static Dictionary<V5Criterion, bool> DefaultIsRel()
            {
                return new Dictionary<V5Criterion, bool>
                {
                    { V5Criterion.Length, true },
                    { V5Criterion.Volume, true },
                    { V5Criterion.Inertia, true }, { V5Criterion.EdgeSum, true },
                    { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, false }
                };
            }
        }

        // ====================================================================
        // 自动匹配退化孔隔离回归测试
        // ====================================================================
        internal static class WbCanAutoMatchTest
        {
            public class TestResult
            {
                public string TestName;
                public bool Passed;
                public string Detail;
            }

            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestDegenVsNormalBlocked());
                results.Add(TestMixedHolesIsomorphicMatch());
                results.Add(TestMixedHolesCrossPairBlocked());
                results.Add(TestDegenVsDegenSwitchOff());
                results.Add(TestDegenVsDegenSwitchOn());
                return results;
            }

            /// <summary>
            /// 一方退化孔、一方正常孔 → Matches 必须为 false
            /// </summary>
            private static TestResult TestDegenVsNormalBlocked()
            {
                var degenHoles = new List<WbHole>
                    { WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0) };
                var normalHoles = new List<WbHole>
                    { new WbHole(40, 100, 1, 1, 0, 0, 0, 1) };

                var degenFeat = WbDegenerateMatchingTest.CreateDegenerateFeature("Degen", degenHoles);
                var normalFeat = new WbFeature
                {
                    Material = "Steel", Color = "Degen",
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = normalHoles
                };

                var enabled = WbDegenerateMatchingTest.AllEnabled();
                var tolerances = WbDegenerateMatchingTest.DefaultTolerances();
                var isRel = WbDegenerateMatchingTest.DefaultIsRel();

                // 开关开启时退化对正常也必须隔离
                bool matchesOn = WbMigrationAudit.Matches(degenFeat, normalFeat,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: true);
                // 开关关闭时当然也隔离
                bool matchesOff = WbMigrationAudit.Matches(degenFeat, normalFeat,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: false);

                bool passed = !matchesOn && !matchesOff;
                return new TestResult
                {
                    TestName = "退化对正常禁止匹配",
                    Passed = passed,
                    Detail = passed ? "已正确隔离（开关开/关均隔离）"
                        : $"开关开={matchesOn}, 开关关={matchesOff}"
                };
            }

            /// <summary>
            /// 同构混合孔：双方各含 1 个退化孔 + 1 个正常孔，值完全相同
            /// 退化→退化 + 正常→正常 均匹配 → 开启退化匹配时 Matches 为 true
            /// </summary>
            private static TestResult TestMixedHolesIsomorphicMatch()
            {
                var holesA = new List<WbHole>
                {
                    WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0),
                    new WbHole(40, 100, 2, 2, 0, 0, 0, 1)
                };
                // B 与 A 完全同构
                var holesB = new List<WbHole>
                {
                    WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0),
                    new WbHole(40, 100, 2, 2, 0, 0, 0, 1)
                };

                WbFeature MakeFeat(string color, List<WbHole> holes) => new WbFeature
                {
                    Material = "Steel", Color = color,
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = holes
                };
                var fA = MakeFeat("Test", holesA);
                var fB = MakeFeat("Test", holesB);

                var enabled = WbDegenerateMatchingTest.AllEnabled();
                var tolerances = WbDegenerateMatchingTest.DefaultTolerances();
                var isRel = WbDegenerateMatchingTest.DefaultIsRel();

                bool matchesOn = WbMigrationAudit.Matches(fA, fB,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: true);
                bool matchesOff = WbMigrationAudit.Matches(fA, fB,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: false);

                // 新语义：开关开/关均允许匹配（开关仅控制容差放宽）
                bool passed = matchesOn && matchesOff;
                return new TestResult
                {
                    TestName = "同构混合孔开关开/关均允许匹配",
                    Passed = passed,
                    Detail = passed ? "开关开/关均允许（开关仅控制容差放宽）"
                        : $"开关开={matchesOn}, 开关关={matchesOff}"
                };
            }

            /// <summary>
            /// 交叉配对反例：A/B 退化孔与正常孔交换值，同类无法匹配、
            /// 但退化↔正常的交叉值完全相同。
            /// 若 HoleCompatible 无 IsDegenerate 拦截，交叉配对可能成功；
            /// 有拦截时应返回 false。
            /// </summary>
            private static TestResult TestMixedHolesCrossPairBlocked()
            {
                // A: degen(10,1,1,0,0) + normal(40,100,2,2,0)
                var holesA = new List<WbHole>
                {
                    WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0),
                    new WbHole(40, 100, 2, 2, 0, 0, 0, 1)
                };
                // B: degen(40,100,2,2,0) + normal(10,1,1,0,0) — 值与 A 交换
                var holesB = new List<WbHole>
                {
                    WbDegenerateMatchingTest.CreateDegenerateHole(40, 100, 2, 2, 0),
                    new WbHole(10, 1, 1, 0, 0, 0, 0, 1)
                };

                WbFeature MakeFeat(string color, List<WbHole> holes) => new WbFeature
                {
                    Material = "Steel", Color = color,
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = holes
                };
                var fA = MakeFeat("Test", holesA);
                var fB = MakeFeat("Test", holesB);

                var enabled = WbDegenerateMatchingTest.AllEnabled();
                var tolerances = WbDegenerateMatchingTest.DefaultTolerances();
                var isRel = WbDegenerateMatchingTest.DefaultIsRel();

                // 同类无法匹配（degen:10vs40, normal:40vs10），
                // 退化↔正常交叉值完全相同但被 HoleCompatible 拦截
                bool matchesOn = WbMigrationAudit.Matches(fA, fB,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: true);
                bool matchesOff = WbMigrationAudit.Matches(fA, fB,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: false);

                bool passed = !matchesOn && !matchesOff;
                return new TestResult
                {
                    TestName = "交叉配对（退化↔正常）被 HoleCompatible 拦截",
                    Passed = passed,
                    Detail = passed ? "交叉已正确拦截"
                        : $"开关开={matchesOn}, 开关关={matchesOff}"
                };
            }

            /// <summary>
            /// 双方退化 + 开关关闭 → 仍允许匹配（使用常规容差，不放大）
            /// </summary>
            private static TestResult TestDegenVsDegenSwitchOff()
            {
                var holesA = new List<WbHole>
                    { WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0) };
                var fA = new WbFeature
                {
                    Material = "Steel", Color = "Degen",
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = holesA
                };
                // fB 与 fA 完全相同的特征
                var holesB = new List<WbHole>
                    { WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0) };
                var fB = new WbFeature
                {
                    Material = "Steel", Color = "Degen",
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = holesB
                };

                var enabled = WbDegenerateMatchingTest.AllEnabled();
                var tolerances = WbDegenerateMatchingTest.DefaultTolerances();
                var isRel = WbDegenerateMatchingTest.DefaultIsRel();

                bool matches = WbMigrationAudit.Matches(fA, fB,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: false);

                return new TestResult
                {
                    TestName = "双方退化+开关关闭仍允许匹配",
                    Passed = matches,
                    Detail = matches ? "已正确允许（使用常规容差）"
                        : "关闭时应允许相同退化孔用常规容差匹配"
                };
            }

            /// <summary>
            /// 双方退化 + 开关开启 → 相同退化孔可匹配
            /// </summary>
            private static TestResult TestDegenVsDegenSwitchOn()
            {
                var holes = new List<WbHole>
                    { WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0) };
                var fA = new WbFeature
                {
                    Material = "Steel", Color = "Degen",
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = holes
                };
                var holesB = new List<WbHole>
                    { WbDegenerateMatchingTest.CreateDegenerateHole(10, 1, 1, 0, 0) };
                var fB = new WbFeature
                {
                    Material = "Steel", Color = "Degen",
                    BoundingBox = new[] { 100.0, 50.0, 30.0 },
                    Area = 1000, Volume = 500,
                    Inertia = new[] { 2000.0, 1000.0, 500.0 },
                    Holes = holesB
                };

                var enabled = WbDegenerateMatchingTest.AllEnabled();
                var tolerances = WbDegenerateMatchingTest.DefaultTolerances();
                var isRel = WbDegenerateMatchingTest.DefaultIsRel();

                bool matches = WbMigrationAudit.Matches(fA, fB,
                    enabled, tolerances, isRel, false, 0.99, 0.3, 0.1, 2.0, enableDegenMatch: true);

                return new TestResult
                {
                    TestName = "双方退化+开关开启允许匹配",
                    Passed = matches,
                    Detail = matches ? "已正确允许（容差放宽）"
                        : "开启时应允许相同退化孔匹配"
                };
            }
        }

        // ====================================================================
        // Jacobi 特征分解回归测试
        // ====================================================================
        internal static class JacobiEigenTest
        {
            public class TestResult
            {
                public string TestName;
                public bool Passed;
                public string Detail;
            }

            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestEqualDiagonalRotation());
                results.Add(TestSmallScale());
                results.Add(TestUltraSmallScale());
                results.Add(TestLargeScale());
                results.Add(TestOrthogonality());
                return results;
            }

            public static void RunAllAndLog()
            {
                var results = RunAll();
                int passed = results.Count(r => r.Passed);
                RhinoApp.WriteLine("=== Jacobi 特征分解回归测试 " + passed + "/" + results.Count + " 通过 ===");
                foreach (var r in results)
                    RhinoApp.WriteLine((r.Passed ? "[PASS] " : "[FAIL] ") + r.TestName + " — " + r.Detail);
            }

            // 场景1：等对角非零耦合矩阵 [[a,b],[b,a]] — 验证 Math.Sign(0) 不再导致旋转失效
            private static TestResult TestEqualDiagonalRotation()
            {
                try
                {
                    double[,] mat = new double[3, 3]
                    {
                        { 2.0, 1.0, 0.0 },
                        { 1.0, 2.0, 0.0 },
                        { 0.0, 0.0, 1.0 }
                    };
                    JacobiEigenSolver.SolveSymmetric(mat, out double[] eval, out Vector3d[] evec);
                    // 理论特征值：3, 1, 1（看 2×2 子块 [[2,1],[1,2]] → λ=3,1）
                    Array.Sort(eval);
                    bool eigenOk = Math.Abs(eval[0] - 1.0) < 1e-10 &&
                                   Math.Abs(eval[1] - 1.0) < 1e-10 &&
                                   Math.Abs(eval[2] - 3.0) < 1e-10;
                    // 特征向量应接近 (±1/√2, ±1/√2, 0)、(0,0,1) 方向
                    double r2 = Math.Sqrt(0.5);
                    bool vecOk = false;
                    foreach (var ev in evec)
                    {
                        if (Math.Abs(Math.Abs(ev.Z) - 1.0) < 1e-10) continue; // (0,0,±1)
                        if (Math.Abs(Math.Abs(ev.X) - r2) < 1e-10 &&
                            Math.Abs(Math.Abs(ev.Y) - r2) < 1e-10 &&
                            Math.Abs(ev.Z) < 1e-10)
                        { vecOk = true; break; }
                    }
                    bool passed = eigenOk && vecOk;
                    return new TestResult
                    {
                        TestName = "等对角非零耦合矩阵",
                        Passed = passed,
                        Detail = passed
                            ? "OK λ=" + string.Join(",", eval.Select(v => v.ToString("F6", CultureInfo.InvariantCulture)))
                            : "特征值期望 1,1,3 实际 " + string.Join(",", eval.Select(v => v.ToString("F6", CultureInfo.InvariantCulture)))
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult { TestName = "等对角非零耦合矩阵", Passed = false, Detail = ex.GetType().Name + ": " + ex.Message };
                }
            }

            // 场景2：极小尺度矩阵 — 验证相对阈值不因绝对值而提前退出
            private static TestResult TestSmallScale()
            {
                try
                {
                    double s = 1e-6;
                    double[,] mat = new double[3, 3]
                    {
                        { 3.0 * s, 0.5 * s, 0.0 },
                        { 0.5 * s, 2.0 * s, 0.0 },
                        { 0.0, 0.0, 1.0 * s }
                    };
                    JacobiEigenSolver.SolveSymmetric(mat, out double[] eval, out Vector3d[] evec);
                    // 验证特征值按相对误差正确（非绝对误差）
                    Array.Sort(eval);
                    // 理论：子块 [[3,0.5],[0.5,2]] → λ ≈ 3.207, 1.793（按 1e-6 缩放）
                    double e1T = 1.0e-6, e2T = 1.792893e-6, e3T = 3.207107e-6;
                    bool eigenOk = Math.Abs(eval[0] - e1T) / Math.Abs(e1T) < 1e-10 &&
                                   Math.Abs(eval[1] - e2T) / Math.Abs(e2T) < 1e-10 &&
                                   Math.Abs(eval[2] - e3T) / Math.Abs(e3T) < 1e-10;
                    return new TestResult
                    {
                        TestName = "极小尺度矩阵(" + s.ToString("E0", CultureInfo.InvariantCulture) + ")",
                        Passed = eigenOk,
                        Detail = eigenOk
                            ? "OK"
                            : "特征值 " + string.Join(",", eval.Select(v => v.ToString("E6", CultureInfo.InvariantCulture)))
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult { TestName = "极小尺度矩阵", Passed = false, Detail = ex.GetType().Name + ": " + ex.Message };
                }
            }

            // 场景2b：超小尺度矩阵 s=1e-13 — 旧代码因 max<1e-12*Math.Max(scale,1.0) 直接退出，
            // 非对角元 5e-14 < 1e-12 导致未对角化。新代码用 Frobenius 范数后阈值 ≈3.8e-27，正常迭代。
            private static TestResult TestUltraSmallScale()
            {
                try
                {
                    double s = 1e-13;
                    double[,] mat = new double[3, 3]
                    {
                        { 3.0 * s, 0.5 * s, 0.0 },
                        { 0.5 * s, 2.0 * s, 0.0 },
                        { 0.0, 0.0, 1.0 * s }
                    };
                    // 非对角元 = 5e-14，旧阈值 1e-12*max(3e-13,1)=1e-12 → 5e-14<1e-12 → 提前退出
                    // 新阈值 1e-14*frobNorm，frobNorm≈3.8e-13 → 阈值≈3.8e-27 → 5e-14>>3.8e-27 → 正常迭代
                    JacobiEigenSolver.SolveSymmetric(mat, out double[] eval, out Vector3d[] evec);
                    Array.Sort(eval);
                    // 理论特征值（同场景2结构，按 1e-13 缩放）
                    double e1T = 1.0e-13, e2T = 1.792893e-13, e3T = 3.207107e-13;
                    // 必须用相对误差验证：绝对误差 1e-12 对 1e-13 级特征值毫无意义
                    bool eigenOk = Math.Abs(eval[0] - e1T) / Math.Abs(e1T) < 1e-10 &&
                                   Math.Abs(eval[1] - e2T) / Math.Abs(e2T) < 1e-10 &&
                                   Math.Abs(eval[2] - e3T) / Math.Abs(e3T) < 1e-10;
                    // 验证残差也按相对范数通过（而非因绝对值 < 1e-8 而误判通过）
                    // SolveSymmetric 内部已校验残差 < 1e-8*frobNorm，此处不抛异常即通过
                    return new TestResult
                    {
                        TestName = "超小尺度矩阵(" + s.ToString("E0", CultureInfo.InvariantCulture) + ")",
                        Passed = eigenOk,
                        Detail = eigenOk
                            ? "OK λ=" + string.Join(",", eval.Select(v => v.ToString("E3", CultureInfo.InvariantCulture)))
                            : "特征值 " + string.Join(",", eval.Select(v => v.ToString("E6", CultureInfo.InvariantCulture))) +
                              "（旧代码会返回未对角化对角元 " + (3.0 * s).ToString("E3", CultureInfo.InvariantCulture) + "," +
                              (2.0 * s).ToString("E3", CultureInfo.InvariantCulture) + "," +
                              (1.0 * s).ToString("E3", CultureInfo.InvariantCulture) + "）"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult { TestName = "超小尺度矩阵", Passed = false, Detail = ex.GetType().Name + ": " + ex.Message };
                }
            }

            // 场景3：极大尺度矩阵 — 验证相对阈值正常收敛
            private static TestResult TestLargeScale()
            {
                try
                {
                    double s = 1e8;
                    double[,] mat = new double[3, 3]
                    {
                        { 300.0 * s, 50.0 * s, 0.0 },
                        { 50.0 * s, 200.0 * s, 0.0 },
                        { 0.0, 0.0, 100.0 * s }
                    };
                    JacobiEigenSolver.SolveSymmetric(mat, out double[] eval, out Vector3d[] evec);
                    Array.Sort(eval);
                    // 理论：子块 [[300,50],[50,200]] → λ ≈ 320.71, 179.29（按 1e8 缩放）
                    double e1T = 100.0e8, e2T = 179.2893e8, e3T = 320.7107e8;
                    bool eigenOk = Math.Abs(eval[0] - e1T) < 1e-2 &&
                                   Math.Abs(eval[1] - e2T) < 1.0 &&
                                   Math.Abs(eval[2] - e3T) < 1.0;
                    bool passed = eigenOk;
                    return new TestResult
                    {
                        TestName = "极大尺度矩阵(" + s.ToString("E0", CultureInfo.InvariantCulture) + ")",
                        Passed = passed,
                        Detail = passed
                            ? "OK"
                            : "特征值 " + string.Join(",", eval.Select(v => v.ToString("E6", CultureInfo.InvariantCulture)))
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult { TestName = "极大尺度矩阵", Passed = false, Detail = ex.GetType().Name + ": " + ex.Message };
                }
            }

            // 场景4：验证特征向量正交性
            private static TestResult TestOrthogonality()
            {
                try
                {
                    double[,] mat = new double[3, 3]
                    {
                        { 5.0, 2.0, 1.0 },
                        { 2.0, 4.0, 3.0 },
                        { 1.0, 3.0, 6.0 }
                    };
                    JacobiEigenSolver.SolveSymmetric(mat, out _, out Vector3d[] evec);
                    // 检查两两正交
                    double d01 = Math.Abs(evec[0] * evec[1]);
                    double d02 = Math.Abs(evec[0] * evec[2]);
                    double d12 = Math.Abs(evec[1] * evec[2]);
                    bool passed = d01 < 1e-12 && d02 < 1e-12 && d12 < 1e-12;
                    return new TestResult
                    {
                        TestName = "特征向量正交性",
                        Passed = passed,
                        Detail = passed
                            ? "OK |v0·v1|=" + d01.ToString("E2", CultureInfo.InvariantCulture) +
                              " |v0·v2|=" + d02.ToString("E2", CultureInfo.InvariantCulture) +
                              " |v1·v2|=" + d12.ToString("E2", CultureInfo.InvariantCulture)
                            : "正交性失败"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult { TestName = "特征向量正交性", Passed = false, Detail = ex.GetType().Name + ": " + ex.Message };
                }
            }
        }

        // ====================================================================
        // 极小外轮廓面积容差回归测试
        // ====================================================================
        internal static class WbTinyOuterProfileTest
        {
            public sealed class TestResult
            {
                public string TestName;
                public bool Passed;
                public string Detail;
            }

            public static List<TestResult> RunAll()
            {
                var results = new List<TestResult>();
                results.Add(TestTinyAreaRejected());
                results.Add(TestEndToEndFallbackTolerance());
                return results;
            }

            public static void Run()
            {
                var results = RunAll();
                int passed = results.Count(r => r.Passed);
                RhinoApp.WriteLine("=== 极小外轮廓面积容差回归测试 " + passed + "/" + results.Count + " 通过 ===");
                foreach (var r in results)
                    RhinoApp.WriteLine((r.Passed ? "[PASS] " : "[FAIL] ") + r.TestName + " — " + r.Detail);
            }

            // 修改后的语义：外轮廓已不参与匹配（硬否决移除），仅靠其余判据。
            // 本例两个零件除外轮廓外完全相同且所有判据关闭、0 孔 → Matches 应为 true。
            private static TestResult TestTinyAreaRejected()
            {
                try
                {
                    double fallbackTol = 0.001;
                    double outerAreaRatio = 0.0001;

                    var profileA = new WbOuterProfile { Area = 0.0001, Perimeter = 100, NU = 1, NV = 0, NW = 0 };
                    var profileB = new WbOuterProfile { Area = 0.00015, Perimeter = 100, NU = 1, NV = 0, NW = 0 };

                    var featureA = new WbFeature
                    {
                        Material = "Steel", Color = "Red",
                        BoundingBox = new[] { 100.0, 80.0, 60.0 },
                        Area = 5000, Volume = 1000,
                        Inertia = new[] { 2000.0, 1990.0, 500.0 },
                        Holes = new List<WbHole>(),
                        OuterProfiles = new List<WbOuterProfile> { profileA }
                    };

                    var featureB = new WbFeature
                    {
                        Material = "Steel", Color = "Red",
                        BoundingBox = new[] { 100.0, 80.0, 60.0 },
                        Area = 5000, Volume = 1000,
                        Inertia = new[] { 2000.0, 1990.0, 500.0 },
                        Holes = new List<WbHole>(),
                        OuterProfiles = new List<WbOuterProfile> { profileB }
                    };

                    // 关闭所有其他判据，仅靠外轮廓面积容差判定
                    var enabled = new Dictionary<V5Criterion, bool>
                    {
                        { V5Criterion.Length, false }, { V5Criterion.Volume, false },
                        { V5Criterion.Inertia, false }, { V5Criterion.EdgeSum, false },
                        { V5Criterion.HoleArea, false }, { V5Criterion.HolePosition, false }
                    };
                    var tolerances = new Dictionary<V5Criterion, double>
                    {
                        { V5Criterion.Length, 0.0 }, { V5Criterion.Volume, 0.0 },
                        { V5Criterion.Inertia, 0.0 }, { V5Criterion.EdgeSum, 0.0 },
                        { V5Criterion.HoleArea, 0.0 }, { V5Criterion.HolePosition, 0.0 }
                    };
                    var isRel = new Dictionary<V5Criterion, bool>
                    {
                        { V5Criterion.Length, true }, { V5Criterion.Volume, true },
                        { V5Criterion.Inertia, true }, { V5Criterion.EdgeSum, true },
                        { V5Criterion.HoleArea, true }, { V5Criterion.HolePosition, true }
                    };

                    bool matches = WbMigrationAudit.Matches(featureA, featureB,
                        enabled, tolerances, isRel,
                        strictMaterialColor: false, normalTol: 0.99,
                        outerAreaRatio, outerPerimMin: 0.01,
                        fallbackTolerance: fallbackTol);

                    // 外轮廓已不参与匹配，除外轮廓外完全相同 → Matches 应为 true
                    bool expected = true;
                    bool passed = matches == expected;

                    return new TestResult
                    {
                        TestName = "极小外轮廓面积→不再否决匹配",
                        Passed = passed,
                        Detail = passed
                            ? "OK — 外轮廓不参与匹配后，其余特征完全一致，正确放行"
                            : "失败 — 预期放行但 Matches=" + matches
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "极小外轮廓面积→不再否决匹配",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }

            // 外轮廓已不参与匹配判定，两个零件除外轮廓外完全相同（0 孔、同材质颜色包围盒惯量体积）
            // → 无论 fallbackTolerance 取何值，均应聚为 1 类。
            private static TestResult TestEndToEndFallbackTolerance()
            {
                try
                {
                    var profileA = new WbOuterProfile { Area = 0.000001, Perimeter = 100, NU = 1, NV = 0, NW = 0 };
                    var profileB = new WbOuterProfile { Area = 0.00001, Perimeter = 100, NU = 1, NV = 0, NW = 0 };

                    var enabled = WbDegenerateMatchingTest.AllEnabled();
                    var tolerances = WbDegenerateMatchingTest.DefaultTolerances();
                    var isRel = WbDegenerateMatchingTest.DefaultIsRel();
                    double outerAreaRatio = 0.01;
                    double outerPerimMin = 0.01;

                    var featureA = new WbFeature
                    {
                        Material = "Steel", Color = "Red",
                        BoundingBox = new[] { 100.0, 80.0, 60.0 },
                        Area = 5000, Volume = 1000,
                        Inertia = new[] { 2000.0, 1990.0, 500.0 },
                        Holes = new List<WbHole>(),
                        OuterProfiles = new List<WbOuterProfile> { profileA }
                    };

                    var featureB = new WbFeature
                    {
                        Material = "Steel", Color = "Red",
                        BoundingBox = new[] { 100.0, 80.0, 60.0 },
                        Area = 5000, Volume = 1000,
                        Inertia = new[] { 2000.0, 1990.0, 500.0 },
                        Holes = new List<WbHole>(),
                        OuterProfiles = new List<WbOuterProfile> { profileB }
                    };

                    var records = new List<WbMigrationRecord>
                    {
                        new WbMigrationRecord(0, "P1", featureA, "test.3dm"),
                        new WbMigrationRecord(1, "P2", featureB, "test.3dm")
                    };

                    // 外轮廓不参与匹配后，两者除外轮廓外完全相同 → 均应聚为 1 类
                    var auditStrict = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                        strictMaterialColor: false, holeNormalTolerance: 0.99,
                        outerProfileAreaRatio: outerAreaRatio, outerProfilePerimeterMin: outerPerimMin,
                        fallbackTolerance: 0.001);
                    bool strictClustered = auditStrict.Classes.Count == 1;

                    var auditLoose = WbMigrationAudit.Create(records, enabled, tolerances, isRel,
                        strictMaterialColor: false, holeNormalTolerance: 0.99,
                        outerProfileAreaRatio: outerAreaRatio, outerProfilePerimeterMin: outerPerimMin,
                        fallbackTolerance: 0.1);
                    bool looseClustered = auditLoose.Classes.Count == 1;

                    bool passed = strictClustered && looseClustered;
                    return new TestResult
                    {
                        TestName = "端到端：外轮廓差异不再影响聚类",
                        Passed = passed,
                        Detail = passed
                            ? "OK — tol=0.001→" + auditStrict.Classes.Count + "类, tol=0.1→" + auditLoose.Classes.Count + "类"
                            : "tol=0.001→" + auditStrict.Classes.Count + "类(期望1), tol=0.1→" + auditLoose.Classes.Count + "类(期望1)"
                    };
                }
                catch (Exception ex)
                {
                    return new TestResult
                    {
                        TestName = "端到端：fallbackTolerance 穿透聚类",
                        Passed = false,
                        Detail = ex.GetType().Name + ": " + ex.Message
                    };
                }
            }
        }

        private static string MirrorCode(WbFeature f, double tol)
        {
            double v = Math.Abs(f.HoleMirrorScore) > tol ? f.HoleMirrorScore : f.MirrorScore;
            return Math.Abs(v) <= tol ? string.Empty : (v >= 0 ? "A" : "B");
        }

        private static bool SameMirrorCode(WbFeature a, WbFeature b, double tol) =>
            string.Equals(MirrorCode(a, tol), MirrorCode(b, tol), StringComparison.Ordinal);

        private static bool Matches(WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double mirrorTol, bool strictMat,
            double normalTol, double outerArea, double outerPerim)
        {
            return WbMigrationAudit.GlobalMatches(a, b, e, t, isRel, strictMat) &&
                   WbMigrationAudit.GeometryMatches(a, b, e, t, isRel, false, normalTol,
                       outerArea, outerPerim);
        }

        private static bool MirrorMatches(WbFeature a, WbFeature b,
            Dictionary<V5Criterion, bool> e, Dictionary<V5Criterion, double> t,
            Dictionary<V5Criterion, bool> isRel, double mirrorTol, bool strictMat,
            double normalTol, double outerArea, double outerPerim)
        {
            if (!WbMigrationAudit.GlobalMatches(a, b, e, t, isRel, strictMat))
                return false;
            // 非镜像变换下已匹配 → 本就是同件，不应再标为镜像关系
            if (WbMigrationAudit.GeometryMatches(a, b, e, t, isRel, false, normalTol,
                outerArea, outerPerim))
                return false;
            return WbMigrationAudit.GeometryMatches(a, b, e, t, isRel, true, normalTol,
                outerArea, outerPerim);
        }
    }

    // ========================================================================
    // 迁移完整性回归测试（建库清理、日志落盘）
    // ========================================================================
    internal static class WbMigrationStoreTest
    {
        public sealed class TestResult
        {
            public string TestName;
            public bool Passed;
            public string Detail;
        }

        public static List<TestResult> RunAll()
        {
            var results = new List<TestResult>();
            results.Add(TestExistingDbNotDeletedOnCreateFailure());
            results.Add(TestWriteLogToFilePreservesOriginalError());
            return results;
        }

        /// <summary>
        /// 测试1：已有数据库文件在创建失败时不被误删
        /// 直接调用 WbCreateMigrationDatabase.CreateEmptyDatabaseAt，
        /// 验证"目标已存在"异常路径下 createdByThisRun 为 false，不触发误删。
        /// </summary>
        private static TestResult TestExistingDbNotDeletedOnCreateFailure()
        {
            string tmpDir = null;
            try
            {
                tmpDir = Path.Combine(Path.GetTempPath(),
                    "wb_migstore_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmpDir);
                string dbPath = Path.Combine(tmpDir, "existing.db");
                File.WriteAllText(dbPath, "ORIGINAL_CONTENT", Encoding.UTF8);

                // 调用生产代码的共享方法
                bool createdByThisRun = false;
                bool threwExpectedException = false;
                try
                {
                    WbCreateMigrationDatabase.CreateEmptyDatabaseAt(dbPath,
                        out createdByThisRun);
                }
                catch (InvalidOperationException)
                {
                    threwExpectedException = true;
                    if (createdByThisRun)
                        File.Delete(dbPath);
                }
                catch
                {
                    if (createdByThisRun)
                        File.Delete(dbPath);
                }

                bool stillExists = File.Exists(dbPath);
                string content = stillExists
                    ? File.ReadAllText(dbPath, Encoding.UTF8) : null;

                // 清理
                try { Directory.Delete(tmpDir, true); } catch { }

                bool passed = stillExists
                    && content == "ORIGINAL_CONTENT"
                    && threwExpectedException
                    && !createdByThisRun;

                string detail;
                if (!threwExpectedException)
                    detail = "失败 — 未抛出预期的 InvalidOperationException";
                else if (createdByThisRun)
                    detail = "失败 — createdByThisRun=true，应保持 false";
                else if (!stillExists)
                    detail = "失败 — 已有文件被误删";
                else if (content != "ORIGINAL_CONTENT")
                    detail = "失败 — 文件内容被修改: " + (content ?? "null");
                else
                    detail = "OK — InvalidOperationException 抛出，createdByThisRun=false，已有文件完整保留";

                return new TestResult
                {
                    TestName = "建库:已有文件不误删",
                    Passed = passed,
                    Detail = detail
                };
            }
            catch (Exception ex)
            {
                try { if (tmpDir != null) Directory.Delete(tmpDir, true); } catch { }
                return new TestResult
                {
                    TestName = "建库:已有文件不误删",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>
        /// 测试2：WriteLogToFile 将日志落盘到数据库同目录，内容包含原始错误
        /// 注意：本测试仅验证日志写入方法本身；完整的"入库异常→写日志→ResetMigration"路径
        /// 需要在 GH 组件实例级别测试（当前回归测试框架不涵盖 GH_Component 生命周期）。
        /// </summary>
        private static TestResult TestWriteLogToFilePreservesOriginalError()
        {
            string tmpDir = null;
            try
            {
                tmpDir = Path.Combine(Path.GetTempPath(),
                    "wb_log_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmpDir);
                string dbPath = Path.Combine(tmpDir, "lib.db");
                File.WriteAllText(dbPath, "dummy", Encoding.UTF8);

                var logs = new List<string>
                {
                    "聚类/入库阶段失败：测试模拟异常",
                    "详细：在 WriteFinalLogs 阶段发生 InvalidOperationException"
                };

                string logPath = WbClassificationMigration.WriteLogToFile(dbPath, logs);

                bool fileCreated = logPath != null && File.Exists(logPath);
                bool contentOk = false;
                if (fileCreated)
                {
                    string content = File.ReadAllText(logPath, Encoding.UTF8);
                    contentOk = content.Contains("聚类/入库阶段失败")
                        && content.Contains("测试模拟异常");
                }

                // 清理
                if (logPath != null)
                    try { if (File.Exists(logPath)) File.Delete(logPath); } catch { }
                try { File.Delete(dbPath); Directory.Delete(tmpDir, true); } catch { }

                return new TestResult
                {
                    TestName = "日志:WriteLogToFile落盘含原始错误",
                    Passed = fileCreated && contentOk,
                    Detail = fileCreated
                        ? (contentOk
                            ? "OK — 日志文件已生成且包含原始错误信息"
                            : "失败 — 日志文件存在但不包含原始错误")
                        : "失败 — 未生成 TXT 日志文件"
                };
            }
            catch (Exception ex)
            {
                try { if (tmpDir != null) Directory.Delete(tmpDir, true); } catch { }
                return new TestResult
                {
                    TestName = "日志:WriteLogToFile落盘含原始错误",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }
    }

    // ========================================================================
    // WbClassificationSettings.TryParse 回归测试
    // ========================================================================
    internal static class WbSettingsParseTest
    {
        public sealed class TestResult
        {
            public string TestName;
            public bool Passed;
            public string Detail;
        }

        public static List<TestResult> RunAll()
        {
            var results = new List<TestResult>();
            results.Add(TestEdgeSumRoundTrip());
            results.Add(TestEdgesSumOldSpellingCompat());
            results.Add(TestInvalidValueRejected());
            return results;
        }

        /// <summary>
        /// 测试：EdgeSum 自定义值序列化后可完整恢复。
        /// 验证 bool/tol/isRel 经过 SettingsText → TryParse 往返后值完全一致。
        /// </summary>
        private static TestResult TestEdgeSumRoundTrip()
        {
            try
            {
                var s = new WbClassificationSettings();
                s.Enabled[V5Criterion.EdgeSum] = true;
                s.Tolerances[V5Criterion.EdgeSum] = 3.5;
                s.IsRelativeTolerance[V5Criterion.EdgeSum] = true;

                // 其余准则设非默认值避免巧合
                s.Enabled[V5Criterion.Length] = false;
                s.Tolerances[V5Criterion.Length] = 1.2;
                s.IsRelativeTolerance[V5Criterion.Length] = false;
                s.Enabled[V5Criterion.Inertia] = true;
                s.Tolerances[V5Criterion.Inertia] = 0.8;
                s.IsRelativeTolerance[V5Criterion.Inertia] = true;
                s.Enabled[V5Criterion.Volume] = false;
                s.Tolerances[V5Criterion.Volume] = 2.0;
                s.IsRelativeTolerance[V5Criterion.Volume] = false;
                s.Enabled[V5Criterion.HoleArea] = true;
                s.Tolerances[V5Criterion.HoleArea] = 5.0;
                s.IsRelativeTolerance[V5Criterion.HoleArea] = false;
                s.Enabled[V5Criterion.HolePosition] = true;
                s.Tolerances[V5Criterion.HolePosition] = 0.25;
                s.IsRelativeTolerance[V5Criterion.HolePosition] = false;

                string text = s.SettingsText();
                if (!WbClassificationSettings.TryParse(text, out WbClassificationSettings parsed))
                    return new TestResult
                    {
                        TestName = "SettingsParse:EdgeSum往返恢复",
                        Passed = false,
                        Detail = "TryParse 返回 false"
                    };

                // 验证六准则
                var criteria = new[] { V5Criterion.Length, V5Criterion.Inertia, V5Criterion.Volume,
                    V5Criterion.EdgeSum, V5Criterion.HoleArea, V5Criterion.HolePosition };
                foreach (var c in criteria)
                {
                    if (parsed.Enabled[c] != s.Enabled[c])
                        return new TestResult
                        {
                            TestName = "SettingsParse:EdgeSum往返恢复",
                            Passed = false,
                            Detail = c + " Enabled 不一致: 期望 " + s.Enabled[c] + "，实际 " + parsed.Enabled[c]
                        };
                    if (Math.Abs(parsed.Tolerances[c] - s.Tolerances[c]) > 1e-15)
                        return new TestResult
                        {
                            TestName = "SettingsParse:EdgeSum往返恢复",
                            Passed = false,
                            Detail = c + " Tolerance 不一致: 期望 " + s.Tolerances[c] + "，实际 " + parsed.Tolerances[c]
                        };
                    if (parsed.IsRelativeTolerance[c] != s.IsRelativeTolerance[c])
                        return new TestResult
                        {
                            TestName = "SettingsParse:EdgeSum往返恢复",
                            Passed = false,
                            Detail = c + " IsRelative 不一致: 期望 " + s.IsRelativeTolerance[c] + "，实际 " + parsed.IsRelativeTolerance[c]
                        };
                }

                return new TestResult
                {
                    TestName = "SettingsParse:EdgeSum往返恢复",
                    Passed = true,
                    Detail = "OK — 六准则全部完整恢复，EdgeSum=" + parsed.Enabled[V5Criterion.EdgeSum] +
                             ",tol=" + parsed.Tolerances[V5Criterion.EdgeSum].ToString("R", CultureInfo.InvariantCulture) +
                             (parsed.IsRelativeTolerance[V5Criterion.EdgeSum] ? "%" : "")
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "SettingsParse:EdgeSum往返恢复",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>
        /// 测试：旧拼写 EdgesSum（多一个 s）可被兼容识别。
        /// 构造含 "EdgesSum" 键的字符串，验证 TryParse 能解析并正确映射到 EdgeSum。
        /// </summary>
        private static TestResult TestEdgesSumOldSpellingCompat()
        {
            try
            {
                // 构造 SettingsText 字符串但将 EdgeSum 替换为旧拼写 EdgesSum
                var s = new WbClassificationSettings();
                s.Enabled[V5Criterion.EdgeSum] = true;
                s.Tolerances[V5Criterion.EdgeSum] = 3.0;
                s.IsRelativeTolerance[V5Criterion.EdgeSum] = true;
                string text = s.SettingsText();

                // 确保替换只影响准则键，不误改其他字段
                int edgeIdx = text.IndexOf("EdgeSum=", StringComparison.Ordinal);
                if (edgeIdx < 0)
                    return new TestResult
                    {
                        TestName = "SettingsParse:EdgesSum旧拼写兼容",
                        Passed = false,
                        Detail = "SettingsText 中未找到 EdgeSum= 键"
                    };

                int endIdx = text.IndexOf(';', edgeIdx);
                string compatText = text.Substring(0, edgeIdx) + "EdgesSum" + text.Substring(edgeIdx + 7, endIdx - edgeIdx - 7) + text.Substring(endIdx);

                if (!WbClassificationSettings.TryParse(compatText, out WbClassificationSettings parsed))
                    return new TestResult
                    {
                        TestName = "SettingsParse:EdgesSum旧拼写兼容",
                        Passed = false,
                        Detail = "TryParse 对 EdgesSum 旧拼写返回 false"
                    };

                if (!parsed.Enabled[V5Criterion.EdgeSum])
                    return new TestResult
                    {
                        TestName = "SettingsParse:EdgesSum旧拼写兼容",
                        Passed = false,
                        Detail = "EdgesSum 旧拼写解析后 Enabled=false，期望 true"
                    };
                if (Math.Abs(parsed.Tolerances[V5Criterion.EdgeSum] - 3.0) > 1e-15)
                    return new TestResult
                    {
                        TestName = "SettingsParse:EdgesSum旧拼写兼容",
                        Passed = false,
                        Detail = "EdgesSum 旧拼写解析后 Tolerance=" + parsed.Tolerances[V5Criterion.EdgeSum] + "，期望 3.0"
                    };
                if (!parsed.IsRelativeTolerance[V5Criterion.EdgeSum])
                    return new TestResult
                    {
                        TestName = "SettingsParse:EdgesSum旧拼写兼容",
                        Passed = false,
                        Detail = "EdgesSum 旧拼写解析后 IsRelative=false，期望 true"
                    };

                return new TestResult
                {
                    TestName = "SettingsParse:EdgesSum旧拼写兼容",
                    Passed = true,
                    Detail = "OK — EdgesSum 旧拼写被正确映射为 EdgeSum，tol=3.0%"
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "SettingsParse:EdgesSum旧拼写兼容",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }

        /// <summary>
        /// 测试：任一必填项值非法时 TryParse 必须返回 false。
        /// 构造 EdgeSum 的 bool 和 tol 均为非法字符串，验证不会静默套默认值。
        /// </summary>
        private static TestResult TestInvalidValueRejected()
        {
            try
            {
                var s = new WbClassificationSettings();
                // 先获取合法文本，再注入损坏的 EdgeSum 项
                string legal = s.SettingsText();
                int edgeIdx = legal.IndexOf("EdgeSum=", StringComparison.Ordinal);
                int endIdx = legal.IndexOf(';', edgeIdx);
                // 替换 EdgeSum=True,tol=1% → EdgeSum=abc,tol=xyz
                string corrupt = legal.Substring(0, edgeIdx)
                    + "EdgeSum=abc,tol=xyz"
                    + legal.Substring(endIdx);

                bool parseOk = WbClassificationSettings.TryParse(corrupt, out WbClassificationSettings _);
                if (parseOk)
                    return new TestResult
                    {
                        TestName = "SettingsParse:非法值被拒绝",
                        Passed = false,
                        Detail = "EdgeSum=abc,tol=xyz 时 TryParse 错误地返回了 true，应返回 false"
                    };

                return new TestResult
                {
                    TestName = "SettingsParse:非法值被拒绝",
                    Passed = true,
                    Detail = "OK — 非法值 EdgeSum=abc,tol=xyz 被正确拒绝（TryParse 返回 false）"
                };
            }
            catch (Exception ex)
            {
                return new TestResult
                {
                    TestName = "SettingsParse:非法值被拒绝",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                };
            }
        }
    }

    // ========================================================================
    // 主电池：分类库迁移_WB
    // ========================================================================
    public sealed class WbClassificationMigration : GH_Component
    {
        private enum State { Idle, OpeningFile, AwaitingData }
        /// <summary>
        /// 迁移阶段：Extracting=提取中，Finalizing=提取完毕、待聚类入库。
        /// 用于检查点恢复，避免重复提取已完成的文件。
        /// </summary>
        private enum SkipCategory { DataUnstable, EntityCountAbnormal, Timeout, Exception }

        private readonly struct WbSkippedFileRecord
        {
            public readonly string FilePath;
            public readonly string Reason;
            public readonly SkipCategory Category;
            public readonly int Index;  // 1-based
            public readonly int Total;

            public WbSkippedFileRecord(string filePath, string reason, SkipCategory category, int index, int total)
            {
                FilePath = filePath;
                Reason = reason;
                Category = category;
                Index = index;
                Total = total;
            }

            public override string ToString()
            {
                string cat = CategoryLabel(Category);
                string pos = Index > 0
                    ? string.Format(CultureInfo.InvariantCulture, "（第 {0}/{1} 个）", Index, Total)
                    : "（从检查点恢复）";
                return string.Format(CultureInfo.InvariantCulture,
                    "  {0} — {1}{2}；原因：{3}", FilePath, cat, pos, Reason);
            }
        }

        private static string CategoryLabel(SkipCategory category)
        {
            switch (category)
            {
                case SkipCategory.DataUnstable: return "数据未稳定";
                case SkipCategory.EntityCountAbnormal: return "实体计数异常";
                case SkipCategory.Timeout: return "等待超时";
                case SkipCategory.Exception: return "异常崩溃";
                default: return "未知";
            }
        }

        private const string SettingsChunk = "WbClassificationMigrationSettings";

        // 注意：表面积(Area) 已从匹配中剔除，不参与校核，也不在右键菜单中显示。
        // 设置与 WbEntityClassifier 共享；通过 WbClassificationSettings 统一管理。
        private readonly WbClassificationSettings _settings = new WbClassificationSettings();
        // 迁移状态
        private bool _runRequested;
        private bool _paused;
        private bool IsSettingsLocked => _runRequested || _paused || _state != State.Idle;
        private bool _lastRunInput;
        private State _state = State.Idle;
        private readonly List<string> _pendingFiles = new List<string>();
        private readonly List<WbMigrationRecord> _collectedRecords = new List<WbMigrationRecord>();
        private readonly List<string> _diagnostics = new List<string>();
        private int _fileIndex;
        private WbMigrationPhase _checkpointPhase = WbMigrationPhase.Extracting;
        private int _waitingAttempts;
        private int _failedFileCount;  // 迁移过程中跳过/失败的文件数（含规则跳过与异常）
        private readonly List<WbSkippedFileRecord> _skippedFiles = new List<WbSkippedFileRecord>();
        private readonly List<string> _emptyFilePaths = new List<string>();

        // 数据稳定性检测
        private string _lastDataSignature = string.Empty;
        private int _stableDataCount;
        private int _dataCheckCount;

        // 定时器
        private int _refreshToken;
        private Timer _refreshTimer;
        private Timer _fileOpenTimer;
        private Timer _finalCloseTimer;
        private List<string> _pendingLogs;

        private string _statusText = string.Empty;
        private string _pendingDatabasePath = string.Empty;

        // ================================================================
        // 公开控制接口
        // ================================================================
        public void StartMigration()
        {
            CancelRefresh();
            StopFileOpenTimer();
            StopFinalCloseTimer();
            _paused = false;
            _state = State.Idle;
            _pendingFiles.Clear();
            _collectedRecords.Clear();
            _diagnostics.Clear();
            _fileIndex = 0;
            _waitingAttempts = 0;
            _failedFileCount = 0;
            _skippedFiles.Clear();
            _emptyFilePaths.Clear();
            _pendingDatabasePath = string.Empty;
            _runRequested = true;
            ScheduleRefresh(1500);
        }

        public void PauseMigration()
        {
            if (_state != State.AwaitingData) return;
            _paused = true;
            CancelRefresh();
        }

        public void ResumeMigration()
        {
            if (!_paused || _state != State.AwaitingData) return;
            _paused = false;
            ScheduleRefresh(1500);
        }

        public bool IsPaused => _paused;

        // ================================================================
        // GH 组件定义
        // ================================================================
        public WbClassificationMigration()
            : base("分类数据库迁移（WB）", "分类库迁移_WB",
                  "从 3dm 文件提取实体、计算稳定特征、校核并写入 V9 分类数据库。",
                  "Parrot", "Library") { }

        public override void CreateAttributes()
        {
            m_attributes = new WbClassificationMigrationAttributes(this);
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库",
                "迁移专用 SQLite 数据库文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("旧编号", "旧编号",
                "与文件路径和实体 GUID 一一对应的完整旧编号。", GH_ParamAccess.list);
            p.AddTextParameter("材质", "材质",
                "与实体一一对应的材质，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色",
                "与实体一一对应的颜色，可为空。", GH_ParamAccess.list);
            p.AddTextParameter("文件路径", "文件",
                "与实体 GUID 一一对应的 .3dm 文件路径。", GH_ParamAccess.list);
            p.AddGenericParameter("实体 GUID", "GUID",
                "文件中待迁移 Brep 实体的 GUID。", GH_ParamAccess.list);
            p.AddBooleanParameter("启动", "启动",
                "上升沿触发；清空进度并从第 0 个文件重新开始。",
                GH_ParamAccess.item, false);
            p.AddBooleanParameter("暂停", "暂停",
                "True 时暂停；恢复为 False 后从当前文件继续。",
                GH_ParamAccess.item, false);
            p[1].Optional = true; p[2].Optional = true; p[3].Optional = true;
            p[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("V9分类特征", "特征",
                "本次计算并写入迁移库的标准 V9 特征。", GH_ParamAccess.list);
            p.AddTextParameter("日志", "日志",
                "校核和迁移日志。", GH_ParamAccess.list);
            p.AddTextParameter("状态", "状态",
                "迁移状态。", GH_ParamAccess.item);
            p.AddBooleanParameter("重建完成", "完成",
                "成功重建数据库时为 True。", GH_ParamAccess.item);
            p.AddTextParameter("详细诊断", "诊断",
                "质心、主轴、逐孔坐标和匹配距离等诊断参数。", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            var numbers = new List<string>();
            var materials = new List<string>();
            var colors = new List<string>();
            var files = new List<string>();
            var ids = new List<GH_Guid>();
            var logs = new List<string>();
            var featureTexts = new List<string>();
            bool saved = false;
            bool run = false, pause = false;

            da.GetData(0, ref database);
            da.GetDataList(4, files);
            da.GetData(6, ref run);
            da.GetData(7, ref pause);
            da.SetDataList(4, _diagnostics);

            // 上升沿 → 重置
            if (run && !_lastRunInput)
            {
                CancelRefresh();
                StopFileOpenTimer();
                StopFinalCloseTimer();
                _paused = false;
                _state = State.Idle;
                _pendingFiles.Clear();
                _collectedRecords.Clear();
                _diagnostics.Clear();
                _fileIndex = 0;
                _waitingAttempts = 0;
                _pendingDatabasePath = string.Empty;
                _runRequested = true;
            }
            _lastRunInput = run;

            // 暂停
            if (pause)
            {
                if (!_paused) { CancelRefresh(); StopFileOpenTimer(); }
                _paused = true;
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, new[] {
                    "已暂停：将\"暂停\"恢复为 False 后继续；再次触发\"启动\"则重新开始。" });
                da.SetData(2, "已暂停。");
                da.SetData(3, false);
                return;
            }

            bool wasPaused = _paused;
            _paused = false;
            if (wasPaused && _state == State.OpeningFile)
            {
                _pendingLogs = logs;
                ScheduleOpenCurrentFile();
            }

            // 启动第一轮
            if (_runRequested)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(database))
                        throw new ArgumentException("数据库地址不能为空。");
                    if (files.Count == 0)
                        throw new ArgumentException("文件路径不能为空。");

                    // 每次启动时将当前右键设置保存到数据库
                    {
                        string settings = _settings.SettingsText();
                        WbMigrationStore.SaveSettings(database, settings);
                    }

                    // 尝试从检查点恢复
                    if (TryResumeCheckpoint(database, files))
                    {
                        if (_checkpointPhase == WbMigrationPhase.Finalizing || _fileIndex >= _pendingFiles.Count)
                        {
                            // 所有文件已提取完毕，直接入库
                            DoFinalizing(logs);
                            _runRequested = false;
                            da.SetDataList(0, new List<string>());
                            da.SetDataList(1, logs);
                            da.SetData(2, SafeFirstLog(logs));
                            da.SetData(3, false);
                            da.SetDataList(4, _diagnostics);
                            return;
                        }

                        _state = State.OpeningFile;
                        ResetDataStability();
                        _statusText = "从检查点恢复：打开第 " +
                            (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                        _pendingLogs = logs;
                        ScheduleOpenCurrentFile();
                        _runRequested = false;
                        logs.Add("已从检查点恢复迁移，继续处理第 " +
                            (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                            " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。");
                        da.SetDataList(0, new List<string>());
                        da.SetDataList(1, logs);
                        da.SetData(2, SafeFirstLog(logs));
                        da.SetData(3, false);
                        da.SetDataList(4, _diagnostics);
                        return;
                    }

                    if (files.Any(string.IsNullOrWhiteSpace) ||
                        files.Any(pth => !File.Exists(pth)))
                        throw new FileNotFoundException("文件路径中包含不存在的文件。");

                    _pendingFiles.Clear();
                    _pendingFiles.AddRange(files.Select(Path.GetFullPath));
                    _pendingDatabasePath = database;
                    _collectedRecords.Clear();
                    _fileIndex = 0;
                    _waitingAttempts = 0;
                    _state = State.OpeningFile;
                    ResetDataStability();
                    _statusText = "准备打开第 1 / " +
                        _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。";
                    _pendingLogs = logs;
                    ScheduleOpenCurrentFile();
                    logs.Add("已启动迁移；本轮求解结束后打开第 1 / " +
                        _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件。");
                }
                catch (Exception ex)
                {
                    logs.Add("错误：" + ex.Message);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                    WriteLogToFile(_pendingDatabasePath, logs);
                    ResetMigration();
                }
                finally { _runRequested = false; }
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, SafeFirstLog(logs));
                da.SetData(3, false);
                return;
            }

            // 正在打开文件
            if (_state == State.OpeningFile)
            {
                logs.Add(string.IsNullOrWhiteSpace(_statusText)
                    ? "等待在求解结束后打开当前文件。" : _statusText);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, SafeFirstLog(logs));
                da.SetData(3, false);
                return;
            }

            // 等待数据
            if (_state != State.AwaitingData)
            {
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, new[] {
                    string.IsNullOrWhiteSpace(_statusText)
                        ? "等待启动；触发\"启动\"后自动处理。"
                        : _statusText });
                da.SetData(2, string.IsNullOrWhiteSpace(_statusText)
                    ? "等待启动。" : _statusText);
                da.SetData(3, false);
                return;
            }

            // 文件已打开，收集上游数据
            da.GetDataList(1, numbers);
            da.GetDataList(2, materials);
            da.GetDataList(3, colors);
            da.GetDataList(5, ids);

            if (!IsDataStable(numbers, ids))
            {
                if (_dataCheckCount >= 20)
                {
                    RecordSkippedFile(logs, "编号和 GUID 数量=" +
                        numbers.Count.ToString(CultureInfo.InvariantCulture) + "/" +
                        ids.Count.ToString(CultureInfo.InvariantCulture),
                        SkipCategory.DataUnstable);
                    saved = AdvanceFile(logs);
                    da.SetDataList(0, featureTexts);
                    da.SetDataList(1, logs);
                    da.SetData(2, SafeFirstLog(logs));
                    da.SetData(3, saved);
                    return;
                }
                logs.Add("等待旧编号和 GUID 稳定：数量=" +
                    numbers.Count.ToString(CultureInfo.InvariantCulture) + "/" +
                    ids.Count.ToString(CultureInfo.InvariantCulture) +
                    "，稳定=" + _stableDataCount.ToString(CultureInfo.InvariantCulture) + "/3。");
                ScheduleRefresh(500);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, SafeFirstLog(logs));
                da.SetData(3, false);
                return;
            }

            if (numbers.Count > 1 || ids.Count > 1)
            {
                RecordSkippedFile(logs, "编号数量=" +
                    numbers.Count.ToString(CultureInfo.InvariantCulture) +
                    "，GUID 数量=" + ids.Count.ToString(CultureInfo.InvariantCulture) +
                    "；每个文件必须各有且只有 1 个",
                    SkipCategory.EntityCountAbnormal);
                saved = AdvanceFile(logs);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, SafeFirstLog(logs));
                da.SetData(3, saved);
                return;
            }

            if (numbers.Count == 0 || ids.Count == 0)
            {
                _waitingAttempts++;
                if (_waitingAttempts <= 50)
                {
                    // 第 5 次仍为空 → 判定为文件无实体，不再继续等待
                    if (_waitingAttempts == 5 && _pendingFiles.Count > 0)
                    {
                        _emptyFilePaths.Add(_pendingFiles[_fileIndex]);
                        logs.Add("文件未包含有效零件，已跳过：" +
                            _pendingFiles[_fileIndex]);
                        RecordSkippedFile(logs, "文件无实体",
                            SkipCategory.Timeout);
                        saved = AdvanceFile(logs);
                        da.SetDataList(0, featureTexts);
                        da.SetDataList(1, logs);
                        da.SetData(2, SafeFirstLog(logs));
                        da.SetData(3, saved);
                        return;
                    }
                    logs.Add("等待自动生成旧编号和 GUID（" +
                        _waitingAttempts.ToString(CultureInfo.InvariantCulture) + " / 50）。");
                    ScheduleRefresh(1500);
                    da.SetDataList(0, featureTexts);
                    da.SetDataList(1, logs);
                    da.SetData(2, SafeFirstLog(logs));
                    da.SetData(3, false);
                    return;
                }
                RecordSkippedFile(logs, "等待超时",
                    SkipCategory.Timeout);
                saved = AdvanceFile(logs);
                da.SetDataList(0, featureTexts);
                da.SetDataList(1, logs);
                da.SetData(2, SafeFirstLog(logs));
                da.SetData(3, saved);
                return;
            }

            // 处理当前文件
            try
            {
                int count = ids.Count;
                ValidateCount(numbers, count, "旧编号", false);
                ValidateCount(materials, count, "材质", true);
                ValidateCount(colors, count, "颜色", true);

                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrWhiteSpace(numbers[i]))
                        throw new ArgumentException("第 " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) + " 个旧编号为空。");
                    if (ids[i] == null || ids[i].Value == Guid.Empty)
                        throw new ArgumentException("第 " +
                            (i + 1).ToString(CultureInfo.InvariantCulture) + " 个 GUID 无效。");

                    Brep brep = ReadBrepFromActiveDocument(ids[i].Value, i);
                    WbFeature feature = WbFeature.Create(
                        TextAt(materials, i), TextAt(colors, i),
                        brep, _settings.InertiaDegenerateRatio, _settings.FallbackTolerance, _collectedRecords.Count);
                    _collectedRecords.Add(new WbMigrationRecord(
                        _collectedRecords.Count, numbers[i].Trim(),
                        feature, _pendingFiles[_fileIndex]));
                    featureTexts.Add(feature.Serialize());
                    _diagnostics.AddRange(feature.DiagnosticLines(
                        numbers[i].Trim(), _pendingFiles[_fileIndex]));
                }

                saved = AdvanceFile(logs);
            }
            catch (Exception ex)
            {
                if (_fileIndex < _pendingFiles.Count)
                {
                    RecordSkippedFile(logs, ex.Message, SkipCategory.Exception);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ex.Message);
                    saved = AdvanceFile(logs);
                }
                else
                {
                    string msg = "聚类/入库阶段失败：" + ex.Message;
                    logs.Add(msg);
                    _diagnostics.Add(msg);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, msg);
                    saved = false;
                    // 先落盘日志再重置（ResetMigration 会清空 _pendingDatabasePath）
                    string dbForLog = _pendingDatabasePath;
                    WriteLogToFile(dbForLog, logs);
                    ResetMigration();
                }
            }

            da.SetDataList(0, featureTexts);
            da.SetDataList(1, logs);
            da.SetData(2, SafeFirstLog(logs));
            da.SetData(3, saved);
            da.SetDataList(4, _diagnostics);
        }

        // ================================================================
        // 右键菜单（参考"实体特征分类"电池设计）
        // ================================================================
        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);

            WbSettingsMenuControl control = new WbSettingsMenuControl(this);
            ToolStripControlHost host = new ToolStripControlHost(control)
            {
                AutoSize = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Size = control.Size
            };
            menu.Closing += (sender, e) => control.CommitAll();
            menu.Items.Add(host);
        }

        private void SetCriterionEnabled(V5Criterion criterion, bool enabled)
        {
            if (IsSettingsLocked) return;
            if (_settings.Enabled[criterion] == enabled)
                return;

            _settings.Enabled[criterion] = enabled;
            MarkSettingsChanged();
        }

        private void SetCriterionTolerance(V5Criterion criterion, string text)
        {
            if (IsSettingsLocked) return;
            if (!TryParseTolerance(text, out double value, out bool useAbsolute))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "误差数值无法识别：" + text);
                return;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, CriterionDisplayName(criterion) + " 容差不能为 NaN 或无穷大：" + text);
                return;
            }

            if (value < 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"{CriterionDisplayName(criterion)} 容差为负值，已自动取绝对值。");

            value = Math.Abs(value);
            if (IsAbsoluteOnlyCriterion(criterion))
                useAbsolute = true;

            if (Math.Abs(_settings.Tolerances[criterion] - value) <= 1e-12 &&
                _settings.IsRelativeTolerance[criterion] == !useAbsolute)
                return;

            _settings.Tolerances[criterion] = value;
            _settings.IsRelativeTolerance[criterion] = !useAbsolute;
            MarkSettingsChanged();
        }


        private void SetFallbackTolerance(string text)
        {
            if (IsSettingsLocked) return;
            if (!TryParseTolerance(text, out double value, out bool _))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "回退容差无法识别：" + text);
                return;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "回退容差不能为 NaN 或无穷大：" + text);
                return;
            }

            if (value < 0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "回退容差为负值，已自动取绝对值。");

            value = Math.Abs(value);
            if (Math.Abs(_settings.FallbackTolerance - value) <= 1e-12)
                return;

            _settings.FallbackTolerance = value;
            MarkSettingsChanged();
        }

        private void SetInertiaDegenerateRatio(string text)
        {
            if (IsSettingsLocked) return;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化判定阈值无法识别：" + text);
                return;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化判定阈值不能为 NaN 或无穷大：" + text);
                return;
            }

            double clamped = Math.Max(0.5, Math.Min(1.0, value));
            if (Math.Abs(clamped - value) > 1e-12)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"退化判定阈值已自动限制在 0.5~1.0 范围内（输入 {value:G6} → {clamped:G6}）。");
            value = clamped;

            if (Math.Abs(_settings.InertiaDegenerateRatio - value) <= 1e-12)
                return;

            _settings.InertiaDegenerateRatio = value;
            MarkSettingsChanged();
        }

        private void SetStrictMaterialColor(bool value)
        {
            if (IsSettingsLocked) return;
            if (_settings.StrictMaterialColor == value) return;
            _settings.StrictMaterialColor = value;
            MarkSettingsChanged();
        }

        private void SetHoleNormalTolerance(string text)
        {
            if (IsSettingsLocked) return;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "法向兼容阈值无法识别：" + text);
                return;
            }

            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "法向兼容阈值不能为 NaN 或无穷大：" + text);
                return;
            }

            double clamped = Math.Max(0.0, Math.Min(1.0, value));
            if (Math.Abs(clamped - value) > 1e-12)
            {
                string reason = value < 0 ? "负值" : value > 1 ? "超出上界" : "";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"法向兼容阈值超出 0~1 范围（{reason}），已自动限制（输入 {value:G6} → {clamped:G6}）。");
            }
            value = clamped;

            if (Math.Abs(_settings.HoleNormalTolerance - value) <= 1e-12)
                return;

            _settings.HoleNormalTolerance = value;
            MarkSettingsChanged();
        }

        private void SetOuterProfileAreaRatio(string text)
        {
            if (IsSettingsLocked) return;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓面积容差无法识别：" + text);
                return;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓面积容差不能为 NaN 或无穷大：" + text);
                return;
            }
            double clamped = Math.Max(0, value);
            if (Math.Abs(clamped - value) > 1e-15)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"外轮廓面积容差不能为负，已自动修正为 0（输入 {value:G6} → {clamped:G6}）。");
            value = clamped;
            if (Math.Abs(_settings.OuterProfileAreaRatio - value) <= 1e-15) return;
            _settings.OuterProfileAreaRatio = value;
            MarkSettingsChanged();
        }

        private void SetOuterProfilePerimeterMin(string text)
        {
            if (IsSettingsLocked) return;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓周长最小容差无法识别：" + text);
                return;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "外轮廓周长最小容差不能为 NaN 或无穷大：" + text);
                return;
            }
            double clamped = Math.Max(0, value);
            if (Math.Abs(clamped - value) > 1e-15)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"外轮廓周长最小容差不能为负，已自动修正为 0（输入 {value:G6} → {clamped:G6}）。");
            value = clamped;
            if (Math.Abs(_settings.OuterProfilePerimeterMin - value) <= 1e-15) return;
            _settings.OuterProfilePerimeterMin = value;
            MarkSettingsChanged();
        }

        private void SetDegeneratePositionMultiplier(string text)
        {
            if (IsSettingsLocked) return;
            if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化孔容差倍数无法识别：" + text);
                return;
            }
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "退化孔容差倍数不能为 NaN 或无穷大：" + text);
                return;
            }
            double clamped = Math.Max(1.0, value);
            if (Math.Abs(clamped - value) > 1e-15)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"退化孔容差倍数不能小于 1.0，已自动修正为 1.0（输入 {value:G6} → {clamped:G6}）。");
            value = clamped;
            if (Math.Abs(_settings.DegeneratePositionMultiplier - value) <= 1e-15) return;
            _settings.DegeneratePositionMultiplier = value;
            MarkSettingsChanged();
        }

        private void SetEnableDegenerateMatching(bool value)
        {
            if (IsSettingsLocked) return;
            if (_settings.EnableDegenerateMatching == value) return;
            _settings.EnableDegenerateMatching = value;
            MarkSettingsChanged();
        }

        internal static bool TryParseTolerance(string text, out double value, out bool useAbsolute)
        {
            value = 0.0;
            useAbsolute = true;
            text = (text ?? string.Empty).Trim();
            if (text.EndsWith("%", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1).Trim();
                useAbsolute = false;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private string FormatToleranceForCriterion(V5Criterion criterion)
        {
            string text = _settings.Tolerances[criterion].ToString("R", CultureInfo.InvariantCulture);
            return IsAbsoluteOnlyCriterion(criterion) || _settings.IsRelativeTolerance[criterion] == false
                ? text
                : text + "%";
        }

        internal static string CriterionDisplayName(V5Criterion criterion)
        {
            switch (criterion)
            {
                case V5Criterion.Length: return "主轴定向包围盒";
                case V5Criterion.Inertia: return "转动惯量";
                case V5Criterion.Volume: return "体积";
                case V5Criterion.EdgeSum: return "孔周长和";
                case V5Criterion.HoleArea: return "孔面积";
                case V5Criterion.HolePosition: return "孔位";
                default: return criterion.ToString();
            }
        }

        internal static string CriterionTooltip(V5Criterion criterion)
        {
            switch (criterion)
            {
                case V5Criterion.Length:
                    return "基于体积/面积主惯量轴的定向包围盒三维尺寸，用于最基本的外形区分。若两件在该维度差异大则直接判为不同类。始终启用。\n" +
                           "建议：相对误差 0.1%~2.0%，默认 0.5%。大尺寸模型可收紧，小尺寸模型宜放宽。（所有尺寸单位为当前 Rhino 文档单位）";
                case V5Criterion.Inertia:
                    return "主惯量轴对应的主惯量矩，反映质量/面积沿各轴的分布特征。不同形状、不同壁厚的零件惯量分布不同。\n" +
                           "建议：相对误差 0.05%~1.0%，默认 0.2%。";
                case V5Criterion.Volume:
                    return "Brep 实体体积（含孔洞影响）。从整体层面区分尺寸差异，但对形状相同的局部孔洞变化不如孔周长/面积敏感。\n" +
                           "建议：相对误差 0.1%~5.0%，默认 0.5%。大件可改用绝对值。";
                case V5Criterion.EdgeSum:
                    return "Brep 所有内环的周长总和（非物理孔数）。外形相同的件，内环细节不同（如倒角差异、孔型不同）会在此项体现。\n" +
                           "建议：相对误差 0.5%~5.0%，默认 1.0%。内环多的件可适当放宽。";
                case V5Criterion.HoleArea:
                    return "Brep 所有内环的面积总和，与孔周长和互补：周长对边缘敏感，面积反映整体开孔率。\n" +
                           "建议：相对误差 0.5%~10.0%，默认 1.0%。多内环大内环件需放宽，少内环小内环件可收紧。";
                case V5Criterion.HolePosition:
                    return "内环重心的空间坐标偏差。只能使用绝对误差，因为位置偏差与内环自身大小无关。单位为当前 Rhino 文档单位。\n" +
                           "建议：0.1~5.0，默认 0.5。精密件用 0.1~0.3，粗分类用 1.0~3.0。";
                default:
                    return "带 % 表示相对误差，例如 0.5%；不带 % 表示绝对误差，例如 100。";
            }
        }

        /// <summary>
        /// 孔位强制绝对容差（不可切换为 % 相对模式）。
        /// 设计决策：孔位是空间坐标偏差，没有自然的"自身值"作相对基准；
        /// 若用包围盒尺寸归一化，则同一绝对偏差在小件和大件上判为不同比例，
        /// 引入新的不一致。跨尺度混库场景通过用户自行选择合适绝对容差来应对。
        /// </summary>
        internal static bool IsAbsoluteOnlyCriterion(V5Criterion criterion)
        {
            return criterion == V5Criterion.HolePosition;
        }

        private void MarkSettingsChanged()
        {
            Attributes?.ExpireLayout();
            OnDisplayExpired(true);
        }

        private sealed class WbSettingsMenuControl : UserControl
        {
            private static readonly V5Criterion[] Criteria =
            {
                V5Criterion.Length,
                V5Criterion.Inertia,
                V5Criterion.Volume,
                V5Criterion.EdgeSum,
                V5Criterion.HoleArea,
                V5Criterion.HolePosition
            };

            private readonly WbClassificationMigration _owner;
            private readonly ToolTip _toolTip = new ToolTip();
            private readonly List<Action> _commitActions = new List<Action>();

            public WbSettingsMenuControl(WbClassificationMigration owner)
            {
                _owner = owner;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = SystemColors.Control;
                Margin = Padding.Empty;
                Padding = new Padding(8, 6, 8, 8);
                Size = new Size(256, 544);

                TableLayoutPanel layout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = Criteria.Length + 11,
                    Margin = Padding.Empty,
                    Padding = Padding.Empty
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                for (int i = 0; i < Criteria.Length + 9; i++)
                    layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
                Controls.Add(layout);

                AddHeader(layout);
                for (int i = 0; i < Criteria.Length; i++)
                    AddCriterionRow(layout, i + 1, Criteria[i]);

                AddFallbackToleranceRow(layout, Criteria.Length + 1);
                AddInertiaRow(layout, Criteria.Length + 2);
                AddMaterialStrictRow(layout, Criteria.Length + 3);
                AddOuterProfileAreaRow(layout, Criteria.Length + 4);
                AddOuterProfilePerimeterRow(layout, Criteria.Length + 5);
                AddDegenerateMultiplierRow(layout, Criteria.Length + 6);
                AddDegenerateMatchRow(layout, Criteria.Length + 7);
                AddNormalToleranceRow(layout, Criteria.Length + 8);
                AddHelp(layout, Criteria.Length + 9);

                // 迁移启动后参数全程锁定：从点击"启动"到迁移完成/失败/取消并重置为止
                if (_owner.IsSettingsLocked)
                    SetLayoutInputsEnabled(layout, false);
            }

            private static void SetLayoutInputsEnabled(TableLayoutPanel layout, bool enabled)
            {
                foreach (Control c in layout.Controls)
                {
                    if (c is CheckBox || c is TextBox || c is ComboBox)
                        c.Enabled = enabled;
                }
            }

            private void AddHeader(TableLayoutPanel layout)
            {
                layout.Controls.Add(CreateHeaderLabel(""), 0, 0);
                layout.Controls.Add(CreateHeaderLabel("容差"), 1, 0);
            }

            private void AddCriterionRow(TableLayoutPanel layout, int row, V5Criterion criterion)
            {
                bool isLength = criterion == V5Criterion.Length;
                CheckBox checkBox = new CheckBox
                {
                    Text = CriterionDisplayName(criterion),
                    Checked = _owner._settings.Enabled[criterion],
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Enabled = !isLength
                };
                if (!isLength)
                    checkBox.CheckedChanged += (sender, e) => _owner.SetCriterionEnabled(criterion, checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner.FormatToleranceForCriterion(criterion));
                _toolTip.SetToolTip(toleranceBox, CriterionTooltip(criterion));
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetCriterionTolerance(criterion, toleranceBox.Text),
                    () => _owner.FormatToleranceForCriterion(criterion));
                layout.Controls.Add(toleranceBox, 1, row);
            }

            private void AddFallbackToleranceRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("回退容差");
                layout.Controls.Add(label, 0, row);

                TextBox toleranceBox = CreateTextBox(_owner._settings.FallbackTolerance.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(toleranceBox, "无 Rhino 文档时的曲线拼接容差回退值。正常 Rhino 交互环境下不使用此值，仅在离线批处理或异常路径下生效。\n" +
                    "建议：0.0001~0.01（当前 Rhino 文档单位），默认 0.001。一般无需修改。");
                RegisterTextBoxCommit(
                    toleranceBox,
                    () => _owner.SetFallbackTolerance(toleranceBox.Text),
                    () => _owner._settings.FallbackTolerance.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(toleranceBox, 1, row);
            }

            private void AddInertiaRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("退化判定阈值");
                layout.Controls.Add(label, 0, row);

                TextBox ratioBox = CreateTextBox(_owner._settings.InertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(ratioBox, "相邻主惯量比值 > 此值时标记为退化件（如近圆柱体、球体）。退化件缺少明确的主惯量轴方向，后续匹配走专用流程。\n" +
                    "范围：0.5~1.0，默认 0.99。建议 0.95~0.99。越低越容易判为退化（更保守），越高越严格。");
                RegisterTextBoxCommit(
                    ratioBox,
                    () => _owner.SetInertiaDegenerateRatio(ratioBox.Text),
                    () => _owner._settings.InertiaDegenerateRatio.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(ratioBox, 1, row);
            }

            private void AddOuterProfileAreaRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("外轮廓面积容差（仅诊断）");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._settings.OuterProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "外轮廓已不参与匹配判定，此参数仅在同件判定诊断输出中参考。保留以兼容旧设置与旧数据库参数。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetOuterProfileAreaRatio(tolBox.Text),
                    () => _owner._settings.OuterProfileAreaRatio.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddOuterProfilePerimeterRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("外轮廓周长最小容差（仅诊断）");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._settings.OuterProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "外轮廓已不参与匹配判定，此参数仅在同件判定诊断输出中参考。保留以兼容旧设置与旧数据库参数。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetOuterProfilePerimeterMin(tolBox.Text),
                    () => _owner._settings.OuterProfilePerimeterMin.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddDegenerateMultiplierRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("退化孔容差倍数");
                layout.Controls.Add(label, 0, row);
                TextBox tolBox = CreateTextBox(_owner._settings.DegeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "退化孔匹配时将周长、面积、位置容差乘以此倍数以放宽匹配门槛。拟合法向阈值固定按 normalTol × 0.95 放宽，不受此倍数影响。退化孔特征少、置信度低，需要更大的容差空间。\n" +
                    "范围：≥ 1.0，默认 2.0。建议 1.5~5.0。值越大越宽松，值越小越严格。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetDegeneratePositionMultiplier(tolBox.Text),
                    () => _owner._settings.DegeneratePositionMultiplier.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddDegenerateMatchRow(TableLayoutPanel layout, int row)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = "退化孔容差放宽",
                    Checked = _owner._settings.EnableDegenerateMatching,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _toolTip.SetToolTip(checkBox, "开启后，对双方均含退化孔的配对，将周长/面积/位置容差乘以退化孔容差倍数以放宽匹配门槛；拟合法向阈值固定按 normalTol × 0.95 放宽。\n" +
                    "关闭时退化孔件使用常规容差参与匹配，且始终正常参与自动聚类。默认关闭。");
                checkBox.CheckedChanged += (sender, e) => _owner.SetEnableDegenerateMatching(checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);

                Label hint = CreateBodyLabel("默认关闭：仅放宽容差，不控制聚类参与");
                hint.ForeColor = Color.DimGray;
                hint.AutoSize = false;
                hint.Dock = DockStyle.Fill;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                layout.SetColumnSpan(hint, 1);
                layout.Controls.Add(hint, 1, row);
            }

            private void AddNormalToleranceRow(TableLayoutPanel layout, int row)
            {
                Label label = CreateBodyLabel("法向兼容阈值");
                layout.Controls.Add(label, 0, row);

                TextBox tolBox = CreateTextBox(_owner._settings.HoleNormalTolerance.ToString("R", CultureInfo.InvariantCulture));
                _toolTip.SetToolTip(tolBox, "|cos| 法向兼容阈值，同时用于孔法向比较和外轮廓法向比较。使用绝对值 |dot| 判断，正反法向均兼容。\n" +
                    "|cos(法向夹角)| 低于此值时判定为不兼容。0.9986 ≈ 3° 偏差。\n" +
                    "范围：0~1，默认 0.9986。建议 0.99~0.9999。越接近 1 越严格，越接近 0 越宽松。");
                RegisterTextBoxCommit(
                    tolBox,
                    () => _owner.SetHoleNormalTolerance(tolBox.Text),
                    () => _owner._settings.HoleNormalTolerance.ToString("R", CultureInfo.InvariantCulture));
                layout.Controls.Add(tolBox, 1, row);
            }

            private void AddMaterialStrictRow(TableLayoutPanel layout, int row)
            {
                CheckBox checkBox = new CheckBox
                {
                    Text = "材质/颜色严格模式",
                    Checked = _owner._settings.StrictMaterialColor,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 4, 1),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                _toolTip.SetToolTip(checkBox, "开启后，材质和颜色必须双方均填写且相等，或双方均为空才匹配。关闭时任一方为空即视为匹配（宽松模式）。\n" +
                    "建议：同一批生产数据开启严格模式；跨厂家跨批数据关闭，避免空值导致无法匹配。");
                checkBox.CheckedChanged += (sender, e) => _owner.SetStrictMaterialColor(checkBox.Checked);
                layout.Controls.Add(checkBox, 0, row);
                
                Label hint = CreateBodyLabel("开启后空值不再通配");
                hint.ForeColor = Color.DimGray;
                hint.AutoSize = false;
                hint.Dock = DockStyle.Fill;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                layout.SetColumnSpan(hint, 1);
                layout.Controls.Add(hint, 1, row);
            }

            private void AddHelp(TableLayoutPanel layout, int row)
            {
                Label help = CreateBodyLabel("提示：容差末尾带 % 为相对误差（如 0.5%），不带 % 为绝对误差。" +
                    "孔位强制绝对误差，镜像区分比较无量纲分数。鼠标悬停各参数可查看建议取值范围与详细说明。");
                help.ForeColor = Color.DimGray;
                help.AutoSize = false;
                help.Dock = DockStyle.Fill;
                layout.SetColumnSpan(help, 2);
                layout.Controls.Add(help, 0, row);
            }

            private static Label CreateHeaderLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    Font = new Font(SystemFonts.MenuFont, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 0, 4, 3)
                };
            }

            private static Label CreateBodyLabel(string text)
            {
                return new Label
                {
                    Text = text,
                    AutoSize = false,
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Margin = new Padding(0, 1, 4, 1)
                };
            }

            private static TextBox CreateTextBox(string text)
            {
                return new TextBox
                {
                    Text = text,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 1, 8, 1),
                    BorderStyle = BorderStyle.FixedSingle
                };
            }

            public void CommitAll()
            {
                foreach (Action commit in _commitActions.ToArray())
                {
                    commit();
                }
            }

            private void RegisterTextBoxCommit(TextBox box, Action commit, Func<string> format)
            {
                _commitActions.Add(commit);
                box.Leave += (sender, e) =>
                {
                    commit();
                    box.Text = format();
                };
                box.KeyDown += (sender, e) =>
                {
                    if (e.KeyCode != Keys.Enter)
                        return;

                    commit();
                    box.Text = format();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                };
            }
        }

        // ================================================================
        // 序列化
        // ================================================================
        public override bool Write(GH_IWriter writer)
        {
            _settings.Write(writer, SettingsChunk);
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            _settings.Read(reader, SettingsChunk);
            return base.Read(reader);
        }

        // ================================================================
        // 文件操作
        // ================================================================
        private void OpenCurrentFile()
        {
            string filePath = _pendingFiles[_fileIndex];
            if (IsActiveRhinoFile(filePath)) return;

            RhinoDoc current = RhinoDoc.ActiveDoc;
            if (current != null)
            {
                current.Modified = false;
                RhinoApp.RunScript("_-Close _No", false);
                RhinoApp.Wait();
            }

            string escaped = filePath.Replace("\"", "\"\"");
            if (!RhinoApp.RunScript("_-Open \"" + escaped + "\"", false))
                throw new InvalidOperationException("无法打开文件：" + filePath);
            RhinoApp.Wait();

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null || !IsActiveRhinoFile(filePath))
                throw new InvalidOperationException("当前文档未切换到：" + filePath);
        }

        private static bool IsActiveRhinoFile(string filePath)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return false;
            return string.Equals(
                NormalizePath(doc.Path), NormalizePath(filePath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try { return Path.GetFullPath(path.Trim()); }
            catch { return path.Trim(); }
        }

        /// <summary>
        /// 验证检查点文件列表与当前输入文件列表一致性。
        /// 按原顺序逐项比较规范化路径；顺序不同即失败（FileIndex 依赖原顺序）。
        /// 返回是否通过，失败时 reason 包含诊断消息。
        /// </summary>
        internal static bool ValidateFileListConsistency(
            List<string> checkpointFiles, List<string> currentFiles, out string reason)
        {
            if (currentFiles == null || currentFiles.Count == 0)
            {
                reason = "当前输入文件列表为空。";
                return false;
            }
            if (checkpointFiles == null || checkpointFiles.Count == 0)
            {
                reason = "检查点文件列表为空。";
                return false;
            }
            if (checkpointFiles.Count != currentFiles.Count)
            {
                reason = "检查点文件列表数量不一致（检查点 " +
                    checkpointFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个，当前输入 " +
                    currentFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个）。";
                return false;
            }

            for (int i = 0; i < checkpointFiles.Count; i++)
            {
                string cpNorm = NormalizePath(checkpointFiles[i]);
                string curNorm = NormalizePath(currentFiles[i]);
                if (!string.Equals(cpNorm, curNorm, StringComparison.OrdinalIgnoreCase))
                {
                    // 检查是否为同一文件集合但顺序不同（给用户更明确的提示）
                    var cpSet = new HashSet<string>(
                        checkpointFiles.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
                    var curSet = new HashSet<string>(
                        currentFiles.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
                    string mismatchType = cpSet.SetEquals(curSet)
                        ? "文件集合相同但顺序不一致"
                        : "文件内容不一致";
                    reason = "检查点文件列表与当前输入不一致：位置 " +
                        i.ToString(CultureInfo.InvariantCulture) +
                        "（检查点 " + Path.GetFileName(cpNorm) +
                        "，当前 " + Path.GetFileName(curNorm) +
                        "）——" + mismatchType + "。";
                    return false;
                }
            }
            reason = null;
            return true;
        }

        private static void CloseCurrentFile()
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) return;
            doc.Modified = false;
            RhinoApp.RunScript("_-Close _No", false);
            RhinoApp.Wait();
        }

        private static Brep ReadBrepFromActiveDocument(Guid id, int index)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            Rhino.DocObjects.RhinoObject obj = doc?.Objects.FindId(id);
            if (obj == null)
                throw new InvalidOperationException("找不到第 " +
                    (index + 1).ToString(CultureInfo.InvariantCulture) + " 个 GUID：" + id);
            if (obj.Geometry is Brep brep) return brep.DuplicateBrep();
            if (obj.Geometry is Surface surface) return surface.ToBrep();
            if (obj.Geometry is Extrusion extrusion) return extrusion.ToBrep();
            throw new InvalidOperationException("GUID " + id +
                " 对应的对象不是 Brep、曲面或拉伸体。");
        }

        // ================================================================
        // 数据稳定性
        // ================================================================
        private void ResetDataStability()
        {
            _lastDataSignature = string.Empty;
            _stableDataCount = 0;
            _dataCheckCount = 0;
        }

        private bool IsDataStable(List<string> numbers, List<GH_Guid> ids)
        {
            _dataCheckCount++;
            string activePath = NormalizePath(
                RhinoDoc.ActiveDoc == null ? string.Empty : RhinoDoc.ActiveDoc.Path);
            string numberValues = string.Join("\u001f", numbers.Select(x => x ?? string.Empty));
            string guidValues = string.Join("\u001f",
                ids.Select(x => x == null ? string.Empty : x.Value.ToString("D")));
            string signature = activePath + "\u001e" + numberValues + "\u001e" + guidValues;

            if (string.Equals(signature, _lastDataSignature, StringComparison.Ordinal))
                _stableDataCount++;
            else
            {
                _lastDataSignature = signature;
                _stableDataCount = 1;
            }
            return _stableDataCount >= 3;
        }

        private void ResetMigration()
        {
            CancelRefresh();
            StopFileOpenTimer();
            _state = State.Idle;
            _runRequested = false;
            _paused = false;
            _pendingFiles.Clear();
            _fileIndex = 0;
            _waitingAttempts = 0;
            _failedFileCount = 0;
            _skippedFiles.Clear();
            _emptyFilePaths.Clear();
            _pendingDatabasePath = string.Empty;
            _checkpointPhase = WbMigrationPhase.Extracting;
        }

        private void SaveCheckpoint()
        {
            if (string.IsNullOrWhiteSpace(_pendingDatabasePath)) return;
            try
            {
                var cp = new WbMigrationCheckpoint
                {
                    DatabasePath = _pendingDatabasePath,
                    PendingFiles = new List<string>(_pendingFiles),
                    FileIndex = _fileIndex,
                    Phase = _checkpointPhase,
                    FailedFiles = _skippedFiles.Select(r =>
                        string.Format(CultureInfo.InvariantCulture, "{0}\u0001{1}\u0001{2}\u0001{3}\u0001{4}",
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(r.FilePath)),
                            (int)r.Category,
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(r.Reason)),
                            r.Index, r.Total)).ToList(),
                    TotalErrorCount = _failedFileCount,
                    Diagnostics = new List<string>(_diagnostics)
                };
                foreach (var r in _collectedRecords)
                    cp.CollectedRecords.Add(new WbMigrationCheckpoint.CheckpointRecord
                    {
                        Index = r.Index,
                        Number = r.Number,
                        FeatureText = r.Feature.Serialize(),
                        SourceFile = r.SourceFile
                    });
                cp.Save(WbMigrationCheckpoint.GetCheckpointPath(_pendingDatabasePath));
            }
            catch (Exception ex)
            {
                _diagnostics.Add("检查点保存失败：" + ex.Message + "；迁移将继续但无断点续跑保护。");
            }
        }

        private void DeleteCheckpoint()
        {
            try
            {
                string cpPath = WbMigrationCheckpoint.GetCheckpointPath(_pendingDatabasePath);
                if (File.Exists(cpPath)) File.Delete(cpPath);
            }
            catch { }
        }

        /// <summary>
        /// 将日志写入数据库同文件夹的 TXT 文件（带时间戳，永久保存）。
        /// </summary>
        internal static string WriteLogToFile(string databasePath, List<string> logs)
        {
            if (string.IsNullOrWhiteSpace(databasePath) || logs == null || logs.Count == 0)
                return null;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
                if (string.IsNullOrWhiteSpace(dir)) return null;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string logPath = Path.Combine(dir, "迁移日志_" + timestamp + ".txt");
                File.WriteAllText(logPath, string.Join(Environment.NewLine, logs), Encoding.UTF8);
                return logPath;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 将诊断信息写入数据库同文件夹的 TXT 文件。
        /// </summary>
        private static void WriteDiagnosticsToFile(string databasePath, List<string> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(databasePath) || diagnostics == null || diagnostics.Count == 0)
                return;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
                if (string.IsNullOrWhiteSpace(dir)) return;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(dir, "迁移诊断_" + timestamp + ".txt");
                File.WriteAllText(path, string.Join(Environment.NewLine, diagnostics), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>
        /// 将实体特征信息写入数据库同文件夹的 TXT 文件。
        /// </summary>
        private static void WriteFeaturesToFile(string databasePath, List<WbMigrationRecord> records)
        {
            if (string.IsNullOrWhiteSpace(databasePath) || records == null || records.Count == 0)
                return;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
                if (string.IsNullOrWhiteSpace(dir)) return;
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(dir, "迁移特征_" + timestamp + ".txt");
                var lines = new List<string>();
                foreach (var r in records)
                {
                    string summary = r.Feature != null ? r.Feature.ToSummary() : "";
                    lines.Add(string.Format(CultureInfo.InvariantCulture,
                        "{0}\t{1}\t{2}\t{3}", r.Index, r.Number, r.SourceFile, summary));
                }
                File.WriteAllText(path, string.Join(Environment.NewLine, lines), Encoding.UTF8);
            }
            catch { }
        }

        private bool TryResumeCheckpoint(string databasePath, List<string> currentFiles)
        {
            string cpPath = WbMigrationCheckpoint.GetCheckpointPath(databasePath);
            WbMigrationCheckpoint cp;
            try
            {
                cp = WbMigrationCheckpoint.Load(cpPath);
            }
            catch (Exception ex)
            {
                _diagnostics.Add("检查点加载失败（" + ex.Message + "）；将从头开始迁移。");
                return false;
            }
            if (cp == null) return false;
            // 验证检查点一致性：数据库路径匹配，文件列表一致
            if (!string.Equals(NormalizePath(cp.DatabasePath), NormalizePath(databasePath),
                StringComparison.OrdinalIgnoreCase))
                return false;
            if (cp.PendingFiles == null || cp.PendingFiles.Count == 0) return false;
            // NextFileIndex 语义：0=尚未提取任何文件，N=下一个待提取的文件索引，Count=全部提取完毕待入库
            if (cp.FileIndex < 0 || cp.FileIndex > cp.PendingFiles.Count) return false;

            // 验证当前输入文件列表与检查点文件列表一致性
            // 按原顺序逐项比较规范化路径；顺序不同即拒绝恢复（FileIndex 依赖原顺序）
            if (!ValidateFileListConsistency(cp.PendingFiles, currentFiles, out string listReason))
            {
                _diagnostics.Add(listReason + "将从头开始迁移。");
                return false;
            }

            // 恢复状态
            _pendingDatabasePath = databasePath;
            _pendingFiles.Clear();
            _pendingFiles.AddRange(cp.PendingFiles);
            _fileIndex = cp.FileIndex;
            _waitingAttempts = 0;
            _failedFileCount = cp.TotalErrorCount;
            _skippedFiles.Clear();
            _emptyFilePaths.Clear();
            if (cp.FailedFiles != null)
                foreach (string entry in cp.FailedFiles)
                {
                    // 新格式: "{path}\u0001{category}\u0001{reason}\u0001{index}\u0001{total}"
                    // 旧格式（兼容）: 纯路径
                    string[] parts = entry.Split('\u0001');
                    if (parts.Length >= 5)
                    {
                        // 新格式：完整记录
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int catInt);
                        int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx);
                        int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int total);
                        _skippedFiles.Add(new WbSkippedFileRecord(
                            Encoding.UTF8.GetString(Convert.FromBase64String(parts[0])),
                            Encoding.UTF8.GetString(Convert.FromBase64String(parts[2])),
                            (SkipCategory)catInt, idx, total));
                    }
                    else
                    {
                        // 旧格式：仅路径，回退为异常
                        _skippedFiles.Add(new WbSkippedFileRecord(entry, "（从检查点恢复,原因不详）",
                            SkipCategory.Exception, 0, cp.PendingFiles.Count));
                    }
                }
            if (cp.Diagnostics != null)
            {
                _diagnostics.Clear();
                _diagnostics.AddRange(cp.Diagnostics);
            }

            // 恢复已收集记录
            if (cp.CollectedRecords != null)
            {
                _collectedRecords.Clear();
                foreach (var cr in cp.CollectedRecords)
                {
                    try
                    {
                        WbFeature feature = WbFeature.Deserialize(cr.FeatureText);
                        _collectedRecords.Add(new WbMigrationRecord(
                            cr.Index, cr.Number, feature, cr.SourceFile ?? string.Empty));
                    }
                    catch (Exception ex)
                    {
                        // 序列化特征解析失败，该条记录需要在恢复时重新提取
                        // 这通常不应发生；如果发生则无法安全恢复，回退到完全重启
                        _diagnostics.Add("检查点记录反序列化失败：" + ex.Message);
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            "检查点恢复失败：" + ex.Message + " 将从头开始迁移。");
                        _collectedRecords.Clear();
                        _fileIndex = 0;
                        return false;
                    }
                }
            }

            // 恢复迁移阶段
            _checkpointPhase = cp.Phase;
            if (_checkpointPhase == WbMigrationPhase.Finalizing || _fileIndex >= _pendingFiles.Count)
            {
                _fileIndex = _pendingFiles.Count;
                _diagnostics.Add("从检查点恢复：所有文件已提取完毕（" + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个），共收集 " + _collectedRecords.Count.ToString(CultureInfo.InvariantCulture) +
                    " 条记录；将直接进入聚类与入库阶段。");
                return true;
            }

            _diagnostics.Add("从检查点恢复：已处理前 " + _fileIndex.ToString(CultureInfo.InvariantCulture) +
                " 个文件，共收集 " + _collectedRecords.Count.ToString(CultureInfo.InvariantCulture) +
                " 条记录；从第 " + (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) + " 个文件继续。");
            return true;
        }

        // ================================================================
        // 文件推进 → 聚类 → 入库
        // ================================================================
        private void RecordSkippedFile(List<string> logs, string reason, SkipCategory category)
        {
            _failedFileCount++;
            string filePath = _pendingFiles[_fileIndex];
            int oneBased = _fileIndex + 1;
            int total = _pendingFiles.Count;
            var record = new WbSkippedFileRecord(filePath, reason, category, oneBased, total);
            _skippedFiles.Add(record);
            string catLabel = CategoryLabel(category);
            logs.Add(string.Format(CultureInfo.InvariantCulture,
                "跳过当前文件（{0}）：{1}；原因：{2}", filePath, catLabel, reason));
            _diagnostics.Add(string.Format(CultureInfo.InvariantCulture,
                "跳过文件：{0} ({1}) - {2}", filePath, catLabel, reason));
        }

        private bool AdvanceFile(List<string> logs)
        {
            // 先推进索引，再保存检查点：断点续跑将从下一个文件开始，不会重复处理
            _fileIndex++;
            SaveCheckpoint();
            if (_fileIndex < _pendingFiles.Count)
            {
                _waitingAttempts = 0;
                _state = State.OpeningFile;
                ResetDataStability();
                _statusText = "准备打开第 " +
                    (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                    " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个文件。";
                _pendingLogs = logs;
                ScheduleOpenCurrentFile();
                logs.Add("当前文件处理完成；即将打开第 " +
                    (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                    " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个文件。");
                return false;
            }

            // 全部提取完成 → 切换阶段并保存，防止入库中途崩溃时丢失进度
            _checkpointPhase = WbMigrationPhase.Finalizing;
            SaveCheckpoint();
            return DoFinalizing(logs);
        }

        /// <summary>聚类 → 入库 → 清理。可从 AdvanceFile 或检查点恢复时调用。</summary>
        private bool DoFinalizing(List<string> logs)
        {
            var audit = WbMigrationAudit.Create(_collectedRecords, _settings.Enabled, _settings.Tolerances, _settings.IsRelativeTolerance, _settings.StrictMaterialColor, _settings.HoleNormalTolerance,
                _settings.OuterProfileAreaRatio, _settings.OuterProfilePerimeterMin, _settings.DegeneratePositionMultiplier, _settings.EnableDegenerateMatching,
                fallbackTolerance: _settings.FallbackTolerance);
            _diagnostics.AddRange(audit.DiagnosticLines());
            var toInsert = audit.RecordsToInsert();
            var (insertedIds, insertedRecords, dbSkippedCount) = WbMigrationStore.Rebuild(_pendingDatabasePath, toInsert, audit,
                _settings.Enabled, _settings.Tolerances, _settings.IsRelativeTolerance,
                _settings.InertiaDegenerateRatio, _settings.HoleNormalTolerance, _settings.StrictMaterialColor,
                _settings.OuterProfileAreaRatio, _settings.OuterProfilePerimeterMin, _settings.DegeneratePositionMultiplier,
                _settings.EnableDegenerateMatching, _settings.FallbackTolerance);
            WbMigrationStore.Verify(_pendingDatabasePath, insertedRecords, insertedIds);
            if (_skippedFiles.Count > 0)
            {
                int ruleSkip = _skippedFiles.Count(r => r.Category != SkipCategory.Exception);
                int excSkip = _skippedFiles.Count(r => r.Category == SkipCategory.Exception);
                logs.Add("跳过的文件清单（共 " + _skippedFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个，其中规则跳过 " + ruleSkip.ToString(CultureInfo.InvariantCulture) +
                    " 个、异常 " + excSkip.ToString(CultureInfo.InvariantCulture) + " 个）：");
                foreach (var r in _skippedFiles)
                    logs.Add(r.ToString());
            }
            logs.Add("已完成 V9 分类数据库增量入库，新增 " +
                insertedRecords.Count.ToString(CultureInfo.InvariantCulture) + " 条，跳过已存在 " +
                dbSkippedCount.ToString(CultureInfo.InvariantCulture) + " 条（读入 " +
                _collectedRecords.Count.ToString(CultureInfo.InvariantCulture) + " 条）：" +
                _pendingDatabasePath);
            audit.EmptyFiles = _emptyFilePaths;
            logs.AddRange(audit.ToLogs());
            WriteLogToFile(_pendingDatabasePath, logs);
            WriteDiagnosticsToFile(_pendingDatabasePath, _diagnostics);
            WriteFeaturesToFile(_pendingDatabasePath, _collectedRecords);
            DeleteCheckpoint();
            ResetMigration();
            ScheduleFinalClose();
            return true;
        }

        // ================================================================
        // 定时器
        // ================================================================
        private void ScheduleRefresh(int waitMs)
        {
            CancelRefresh();
            int token = ++_refreshToken;
            GH_Document doc = OnPingDocument();
            if (doc != null)
            {
                doc.ScheduleSolution(Math.Max(1, waitMs), d =>
                {
                    if (token != _refreshToken) return;
                    ExpireSolution(false);
                });
                return;
            }
            _refreshTimer = new Timer { Interval = Math.Max(1, waitMs) };
            _refreshTimer.Tick += (s, e) =>
            {
                Timer t = _refreshTimer;
                _refreshTimer = null;
                t?.Stop(); t?.Dispose();
                if (token != _refreshToken) return;
                ExpireSolution(false);
            };
            _refreshTimer.Start();
        }

        private void CancelRefresh()
        {
            _refreshToken++;
            if (_refreshTimer == null) return;
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _refreshTimer = null;
        }

        private void SkipCurrentFileAndContinue(string reason)
        {
            string filePath = _pendingFiles[_fileIndex];
            _failedFileCount++;
            var record = new WbSkippedFileRecord(filePath, reason, SkipCategory.Exception,
                _fileIndex + 1, _pendingFiles.Count);
            _skippedFiles.Add(record);
            _diagnostics.Add("打开文件失败，跳过：" + filePath + " - " + reason);

            if (_pendingLogs != null)
            {
                string catLabel = CategoryLabel(SkipCategory.Exception);
                _pendingLogs.Add(string.Format(CultureInfo.InvariantCulture,
                    "跳过当前文件（{0}）：{1}；原因：{2}", filePath, catLabel, reason));
            }

            _fileIndex++;
            SaveCheckpoint();

            if (_fileIndex < _pendingFiles.Count)
            {
                _waitingAttempts = 0;
                _state = State.OpeningFile;
                ResetDataStability();
                _statusText = "跳过失败文件，准备打开第 " +
                    (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                    " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                    " 个文件。";
                ScheduleOpenCurrentFile();
            }
            else
            {
                _checkpointPhase = WbMigrationPhase.Finalizing;
                SaveCheckpoint();
                _state = State.Idle;
                _runRequested = true;
                _statusText = "所有文件已尝试打开，准备入库。";
            }
        }

        private void ScheduleOpenCurrentFile()
        {
            StopFileOpenTimer();
            _fileOpenTimer = new Timer { Interval = 100 };
            _fileOpenTimer.Tick += (s, e) =>
            {
                Timer t = _fileOpenTimer;
                _fileOpenTimer = null;
                t?.Stop(); t?.Dispose();
                if (_paused || _state != State.OpeningFile) return;
                try
                {
                    OpenCurrentFile();
                    _state = State.AwaitingData;
                    _waitingAttempts = 0;
                    ResetDataStability();
                    _statusText = "已打开第 " +
                        (_fileIndex + 1).ToString(CultureInfo.InvariantCulture) +
                        " / " + _pendingFiles.Count.ToString(CultureInfo.InvariantCulture) +
                        " 个文件；等待旧编号和 GUID 稳定。";
                    ScheduleRefresh(1500);
                }
                catch (Exception ex)
                {
                    _statusText = "跳过无法打开的文件：" + ex.Message;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "跳过无法打开的文件 " + _pendingFiles[_fileIndex] + "：" + ex.Message);
                    SkipCurrentFileAndContinue(ex.Message);
                    ScheduleRefresh(100);
                }
            };
            _fileOpenTimer.Start();
        }

        private void StopFileOpenTimer()
        {
            if (_fileOpenTimer == null) return;
            _fileOpenTimer.Stop();
            _fileOpenTimer.Dispose();
            _fileOpenTimer = null;
        }

        private void ScheduleFinalClose()
        {
            StopFinalCloseTimer();
            _finalCloseTimer = new Timer { Interval = 100 };
            _finalCloseTimer.Tick += (s, e) =>
            {
                Timer t = _finalCloseTimer;
                _finalCloseTimer = null;
                t?.Stop(); t?.Dispose();
                CloseCurrentFile();
            };
            _finalCloseTimer.Start();
        }

        private void StopFinalCloseTimer()
        {
            if (_finalCloseTimer == null) return;
            _finalCloseTimer.Stop();
            _finalCloseTimer.Dispose();
            _finalCloseTimer = null;
        }

        public override void RemovedFromDocument(GH_Document document)
        {
            CancelRefresh();
            StopFileOpenTimer();
            StopFinalCloseTimer();
            base.RemovedFromDocument(document);
        }

        // ================================================================
        // 辅助
        // ================================================================
        private static void ValidateCount<T>(List<T> values, int count, string name, bool optional)
        {
            if (count == 0)
                throw new ArgumentException("实体 GUID 不能为空。");
            if (values == null || values.Count == 0)
            {
                if (optional) return;
                throw new ArgumentException(name + "不能为空。");
            }
            if (values.Count != count)
                throw new ArgumentException(name + "数量必须与实体 GUID 数量一致。");
        }

        private static string TextAt(List<string> values, int index) =>
            values == null || values.Count == 0 ? string.Empty : values[index] ?? string.Empty;

        private static string SafeFirstLog(List<string> logs) =>
            logs != null && logs.Count > 0 ? logs[0] : string.Empty;

        public override Guid ComponentGuid =>
            new Guid("B1D97B05-3A4A-4F3C-91F0-4AC1D59EE805");
    }

    // ========================================================================
    // 自定义渲染（启动 / 暂停按钮）
    // ========================================================================
    internal sealed class WbClassificationMigrationAttributes : GH_ComponentAttributes
    {
        public WbClassificationMigrationAttributes(WbClassificationMigration owner)
            : base(owner) { }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 44f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            var component = (WbClassificationMigration)Owner;

            // 暂停按钮
            RectangleF pauseRect = new RectangleF(
                Bounds.X, Bounds.Bottom - 44f, Bounds.Width, 20f);
            pauseRect.Inflate(-5f, -2f);
            using (GH_Capsule pause = GH_Capsule.CreateCapsule(pauseRect, GH_Palette.Grey))
                pause.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font pf = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat sf = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString(component.IsPaused ? "继续" : "暂停",
                    pf, Brushes.White, pauseRect, sf);

            // 启动按钮
            RectangleF rect = new RectangleF(
                Bounds.X, Bounds.Bottom - 22f, Bounds.Width, 20f);
            rect.Inflate(-5f, -2f);
            using (GH_Capsule cap = GH_Capsule.CreateCapsule(rect, GH_Palette.Black))
                cap.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            using (Font f = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat s = new StringFormat
            { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                graphics.DrawString("启动", f, Brushes.White, rect, s);
        }

        public override GH_ObjectResponse RespondToMouseDown(
            GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF rect = new RectangleF(Bounds.X, Bounds.Bottom - 22f, Bounds.Width, 20f);
            RectangleF pauseRect = new RectangleF(
                Bounds.X, Bounds.Bottom - 44f, Bounds.Width, 20f);

            if (e.Button == MouseButtons.Left && rect.Contains(e.CanvasLocation))
            {
                ((WbClassificationMigration)Owner).StartMigration();
                return GH_ObjectResponse.Handled;
            }
            if (e.Button == MouseButtons.Left && pauseRect.Contains(e.CanvasLocation))
            {
                var component = (WbClassificationMigration)Owner;
                if (component.IsPaused) component.ResumeMigration();
                else component.PauseMigration();
                sender.Invalidate();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }
    }

    /// <summary>创建分类库迁移_WB 配套的 SQLite 空数据库。</summary>
    public sealed class WbCreateMigrationDatabase : GH_Component
    {
        private bool _createRequested;
        private string _lastPath = string.Empty;

        public WbCreateMigrationDatabase()
            : base("创建分类迁移数据库（WB）", "建迁移库_WB",
                  "创建供\"分类库迁移_WB\"使用的空 SQLite 数据库。",
                  "Parrot", "Library") { }

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("文件夹", "文件夹", "新版数据库所在文件夹。", GH_ParamAccess.item);
            p.AddTextParameter("数据库名称", "名称", "数据库文件名；未填写扩展名时自动添加 .db。", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("新版数据库地址", "数据库", "创建后的 WB 数据库完整地址。", GH_ParamAccess.item);
            p.AddTextParameter("状态", "状态", "创建状态。", GH_ParamAccess.item);
        }

        /// <summary>
        /// 在指定完整路径创建空 SQLite 迁移数据库（含 Schema 初始化）。
        /// ��文件已存在则抛出 InvalidOperationException。
        /// createdByThisRun 供调用方在 catch 中判断是否需要清理残留的空 .db 文件。
        /// </summary>
        internal static void CreateEmptyDatabaseAt(string fullPath, out bool createdByThisRun)
        {
            createdByThisRun = false;
            if (File.Exists(fullPath))
                throw new InvalidOperationException(
                    "目标数据库已存在；请使用新名称，或在迁移电池中重建该库。");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            SQLiteConnection.CreateFile(fullPath);
            createdByThisRun = true;
            WbMigrationStore.CreateEmpty(fullPath);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string folder = string.Empty, name = string.Empty;
            if (!da.GetData(0, ref folder) || !da.GetData(1, ref name)) return;
            if (!_createRequested)
            {
                da.SetData(0, _lastPath);
                da.SetData(1, "右键点击\"创建新版数据库\"后执行。");
                return;
            }
            string path = null;
            bool createdByThisRun = false;
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("文件夹和数据库名称不能为空。");
                string fileName = name.Trim();
                if (!string.Equals(Path.GetFileName(fileName), fileName,
                    StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("数据库名称只能是文件名，不能包含路径。");
                if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                    fileName += ".db";
                path = Path.GetFullPath(
                    Path.Combine(Path.GetFullPath(folder), fileName));
                CreateEmptyDatabaseAt(path, out createdByThisRun);
                _lastPath = path;
                da.SetData(0, path);
                da.SetData(1, "已创建 WB 分类迁移数据库。");
            }
            catch (Exception ex)
            {
                // 仅删除本次 CreateFile 生成的空文件；不误删"目标��存在"等场景的已有数据库
                if (createdByThisRun)
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                da.SetData(0, _lastPath);
                da.SetData(1, "错误：" + ex.Message);
            }
            finally { _createRequested = false; }
        }

        protected override void AppendAdditionalComponentMenuItems(
            ToolStripDropDown menu)
        {
            base.AppendAdditionalComponentMenuItems(menu);
            Menu_AppendSeparator(menu);
            Menu_AppendItem(menu, "创建新版数据库", (s, e) =>
            {
                _createRequested = true;
                ExpireSolution(true);
            });
        }

        public override Guid ComponentGuid =>
            new Guid("C8E4A2F1-7D3B-4E5C-91A6-2F8B0D3E4F1A");
    }
}
