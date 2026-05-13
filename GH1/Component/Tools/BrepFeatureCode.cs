using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CreateBrepCodeDatabase : GH_Component
    {
        public bool RunNow { get; set; }
        private string _lastPath = string.Empty;

        public CreateBrepCodeDatabase()
          : base("CreateBrepCodeDatabase", "CreateBrepDB",
              "Create a SQLite database for BrepCode",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Folder", "Folder", "Database folder path", GH_ParamAccess.item);
            pManager.AddTextParameter("DatabaseName", "Name", "Database file name", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("DatabasePath", "DB", "Created database full path", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            string folder = string.Empty;
            string databaseName = string.Empty;

            if (!DA.GetData(0, ref folder)) return;
            if (!DA.GetData(1, ref databaseName)) return;

            if (!RunNow)
            {
                DA.SetData(0, _lastPath);
                return;
            }

            try
            {
                string fullPath = BrepCodeDatabase.Create(folder, databaseName);
                _lastPath = fullPath;
                RunNow = false;
                DA.SetData(0, fullPath);
            }
            catch (Exception ex)
            {
                RunNow = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new CreateBrepCodeDatabaseAttributes(this);
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("1C99A7F9-E56F-4EC3-9D32-5E0DA15B3C35");
    }

    internal static class BrepCodeDatabase
    {
        public const string TableName = "brep_feature_codes_v2";

        public static string Create(string folder, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Database folder is empty.");
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentException("Database name is empty.");

            string fullFolder = Path.GetFullPath(folder);
            Directory.CreateDirectory(fullFolder);

            string fileName = databaseName.Trim();
            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Database name must be a file name, not a path.");

            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                fileName += ".db";

            string fullPath = Path.GetFullPath(Path.Combine(fullFolder, fileName));

            if (!File.Exists(fullPath))
                SQLiteConnection.CreateFile(fullPath);

            EnsureSchema(fullPath);
            return fullPath;
        }

        public static void Validate(string dbPath)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                throw new ArgumentException("Database path is empty.");

            string fullPath = Path.GetFullPath(dbPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("Database does not exist. Use CreateBrepDB first.", fullPath);

            using (SQLiteConnection connection = OpenConnection(fullPath))
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
                command.Parameters.AddWithValue("@name", TableName);
                object result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                    throw new InvalidOperationException("Database schema is missing. Use CreateBrepDB to create this database.");
            }
        }

        public static SQLiteConnection OpenConnection(string dbPath)
        {
            SQLiteConnection connection = new SQLiteConnection("Data Source=" + dbPath + ";Version=3;");
            connection.Open();
            return connection;
        }

        private static void EnsureSchema(string dbPath)
        {
            using (SQLiteConnection connection = OpenConnection(dbPath))
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    $"CREATE TABLE IF NOT EXISTS {TableName} (" +
                    "id INTEGER PRIMARY KEY AUTOINCREMENT," +
                    "code TEXT NOT NULL UNIQUE," +
                    "base_code TEXT NOT NULL," +
                    "mirror_sign INTEGER NOT NULL," +
                    "feature_code TEXT NOT NULL," +
                    "text_key TEXT NOT NULL," +
                    "volume REAL NOT NULL," +
                    "inertia1 REAL NOT NULL," +
                    "inertia2 REAL NOT NULL," +
                    "inertia3 REAL NOT NULL," +
                    "area REAL NOT NULL," +
                    "edge_length REAL NOT NULL," +
                    "mirror_key REAL NOT NULL," +
                    "created_at TEXT NOT NULL," +
                    "created_by TEXT NOT NULL)";
                command.ExecuteNonQuery();
            }
        }
    }

    internal class CreateBrepCodeDatabaseAttributes : GH_ComponentAttributes
    {
        public CreateBrepCodeDatabaseAttributes(CreateBrepCodeDatabase owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 22.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("Run", font, Brushes.White, buttonRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 22, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                CreateBrepCodeDatabase component = (CreateBrepCodeDatabase)Owner;
                component.RunNow = true;
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }
    }

    public class BrepFeatureCode : GH_Component
    {
        private readonly List<FeatureResult> _lastResults = new List<FeatureResult>();

        public bool UseVolume { get; private set; } = true;
        public bool UseInertia { get; private set; } = true;
        public bool UseArea { get; private set; } = true;
        public bool UseEdgeLength { get; private set; } = true;
        public bool ImportNow { get; set; }
        public List<double> CurrentTolerances { get; private set; } = new List<double> { 0.001 };

        public string SelectionText
        {
            get
            {
                List<string> names = new List<string>();
                if (UseVolume) names.Add("V");
                if (UseInertia) names.Add("I");
                if (UseArea) names.Add("A");
                if (UseEdgeLength) names.Add("E");
                names.Add("Mirror");
                return "Features: " + string.Join(" ", names);
            }
        }

        public BrepFeatureCode()
          : base("BrepFeatureCode", "BrepCode",
              "Code Breps by invariant geometric features and store new codes in SQLite",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "B", "Breps to code", GH_ParamAccess.list);
            pManager.AddTextParameter("Text", "T", "Text keys. Count must match Brep count", GH_ParamAccess.list);
            pManager.AddTextParameter("Database", "DB", "SQLite database path", GH_ParamAccess.item);
            pManager.AddTextParameter("Prefix", "P", "Code prefix", GH_ParamAccess.item, "A");
            pManager.AddIntegerParameter("Digits", "D", "Digit count of numeric part", GH_ParamAccess.item, 2);
            pManager.AddGenericParameter("Tolerance", "Tol", "Percent tolerance. Examples: 0.01, 1%, 0.1", GH_ParamAccess.list);
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Code", "Code", "Brep codes", GH_ParamAccess.list);
            pManager.AddTextParameter("FeatureCode", "Feature", "Feature codes used to create or match codes", GH_ParamAccess.list);
            pManager.AddPlaneParameter("MirrorPlane", "Plane", "Principal-inertia mirror decision planes", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            List<Brep> breps = new List<Brep>();
            List<string> textKeys = new List<string>();
            string dbPath = string.Empty;
            string prefix = "A";
            int digits = 2;
            List<object> toleranceInputs = new List<object>();

            if (!DA.GetDataList(0, breps)) return;
            if (!DA.GetDataList(1, textKeys)) return;
            if (!DA.GetData(2, ref dbPath)) return;
            DA.GetData(3, ref prefix);
            DA.GetData(4, ref digits);
            DA.GetDataList(5, toleranceInputs);

            _lastResults.Clear();

            if (breps.Count != textKeys.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Text count must match Brep count.");
                return;
            }

            if (string.IsNullOrWhiteSpace(dbPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Database path is empty.");
                return;
            }

            digits = Math.Max(1, digits);
            List<double> tolerances = ParseTolerances(toleranceInputs);
            CurrentTolerances = tolerances;

            try
            {
                BrepCodeDatabase.Validate(dbPath);
                List<DbRecord> records = ReadRecords(dbPath);
                HashSet<string> usedBaseCodes = new HashSet<string>(records.Select(r => r.BaseCode), StringComparer.OrdinalIgnoreCase);
                HashSet<string> usedCodes = new HashSet<string>(records.Select(r => r.Code), StringComparer.OrdinalIgnoreCase);

                List<string> codes = new List<string>();
                List<string> featureCodes = new List<string>();
                List<Plane> mirrorPlanes = new List<Plane>();

                for (int i = 0; i < breps.Count; i++)
                {
                    FeatureVector feature = ComputeFeature(breps[i], textKeys[i]);
                    string featureCode = BuildFeatureCode(feature);
                    double mirrorTolerance = MirrorTolerance(tolerances);
                    int mirrorSign = MirrorSign(feature.MirrorKey, mirrorTolerance);

                    List<DbRecord> baseMatches = records
                        .Where(r => IsSameBaseFeature(feature, r, tolerances))
                        .ToList();

                    DbRecord sameMirror = baseMatches.FirstOrDefault(r => IsSameMirror(r, feature, mirrorSign, mirrorTolerance));
                    string baseCode = baseMatches.FirstOrDefault()?.BaseCode;
                    string code;
                    bool isNew;

                    if (sameMirror != null)
                    {
                        code = sameMirror.Code;
                        baseCode = sameMirror.BaseCode;
                        isNew = false;
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(baseCode))
                        {
                            baseCode = NextBaseCode(prefix, digits, usedBaseCodes);
                            code = baseCode;
                        }
                        else
                        {
                            string suffix = NextMirrorSuffix(baseMatches.Select(r => r.Code), baseCode, usedCodes);
                            code = baseCode + suffix;
                        }

                        usedBaseCodes.Add(baseCode);
                        usedCodes.Add(code);
                        isNew = true;
                    }

                    FeatureResult result = new FeatureResult
                    {
                        Code = code,
                        BaseCode = baseCode,
                        MirrorSign = mirrorSign,
                        Feature = feature,
                        FeatureCode = featureCode,
                        IsNew = isNew
                    };

                    _lastResults.Add(result);
                    records.Add(ToDbRecord(result));
                    codes.Add(code);
                    featureCodes.Add(featureCode);
                    mirrorPlanes.Add(feature.MirrorPlane);
                }

                if (ImportNow)
                {
                    int count = InsertNewRecords(dbPath, _lastResults.Where(r => r.IsNew));
                    ImportNow = false;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Inserted " + count + " new records.");
                }

                DA.SetDataList(0, codes);
                DA.SetDataList(1, featureCodes);
                DA.SetDataList(2, mirrorPlanes);
            }
            catch (Exception ex)
            {
                ImportNow = false;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }
        }

        public override void CreateAttributes()
        {
            Attributes = new BrepFeatureCodeAttributes(this);
        }

        protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
        {
            Menu_AppendItem(menu, "Use Volume    " + FormatPercent(ToleranceForFeature(0)), (s, e) => ToggleFeature(nameof(UseVolume)), true, UseVolume);
            Menu_AppendItem(menu, "Use Inertia   " + FormatPercent(ToleranceForFeature(1)), (s, e) => ToggleFeature(nameof(UseInertia)), true, UseInertia);
            Menu_AppendItem(menu, "Use Area      " + FormatPercent(ToleranceForFeature(2)), (s, e) => ToggleFeature(nameof(UseArea)), true, UseArea);
            Menu_AppendItem(menu, "Use Edge Length    " + FormatPercent(ToleranceForFeature(3)), (s, e) => ToggleFeature(nameof(UseEdgeLength)), true, UseEdgeLength);
            ToolStripMenuItem mirrorItem = new ToolStripMenuItem("Use Mirror    " + FormatDouble(MirrorTolerance(CurrentTolerances)))
            {
                Checked = true,
                Enabled = false
            };
            menu.Items.Add(mirrorItem);
        }

        private void ToggleFeature(string name)
        {
            if (name == nameof(UseVolume)) UseVolume = !UseVolume;
            if (name == nameof(UseInertia)) UseInertia = !UseInertia;
            if (name == nameof(UseArea)) UseArea = !UseArea;
            if (name == nameof(UseEdgeLength)) UseEdgeLength = !UseEdgeLength;

            if (!UseVolume && !UseInertia && !UseArea && !UseEdgeLength)
                UseVolume = true;

            ExpireSolution(true);
        }

        private FeatureVector ComputeFeature(Brep brep, string textKey)
        {
            if (brep == null || !brep.IsValid)
                throw new ArgumentException("Invalid Brep.");

            using (VolumeMassProperties volume = VolumeMassProperties.Compute(brep, true, true, true, true))
            using (AreaMassProperties area = AreaMassProperties.Compute(brep))
            {
                if (volume == null)
                    throw new ArgumentException("Cannot compute volume mass properties.");
                if (area == null)
                    throw new ArgumentException("Cannot compute area mass properties.");

                double i1;
                double i2;
                double i3;
                Vector3d a1;
                Vector3d a2;
                Vector3d a3;
                volume.CentroidCoordinatesPrincipalMomentsOfInertia(out i1, out a1, out i2, out a2, out i3, out a3);
                PrincipalFrame principalFrame = BuildPrincipalFrame(brep, volume.Centroid, i1, a1, i2, a2, i3, a3);

                return new FeatureVector
                {
                    TextKey = textKey ?? string.Empty,
                    Volume = Math.Abs(volume.Volume),
                    Inertia = new[] { Math.Abs(i1), Math.Abs(i2), Math.Abs(i3) }.OrderBy(v => v).ToArray(),
                    Area = area.Area,
                    EdgeLength = TotalEdgeLength(brep),
                    MirrorKey = principalFrame.MirrorKey,
                    MirrorScale = principalFrame.MirrorScale,
                    MirrorPlane = principalFrame.MirrorPlane
                };
            }
        }

        private double TotalEdgeLength(Brep brep)
        {
            double total = 0.0;
            foreach (BrepEdge edge in brep.Edges)
            {
                total += edge.GetLength();
            }
            return total;
        }

        private PrincipalFrame BuildPrincipalFrame(Brep brep, Point3d centroid, double i1, Vector3d a1, double i2, Vector3d a2, double i3, Vector3d a3)
        {
            List<Vector3d> vectors = MeshVectorsFromCentroid(brep, centroid);
            if (vectors.Count < 4)
                return PrincipalFrame.Empty(centroid);

            List<PrincipalAxis> axes = new List<PrincipalAxis>
            {
                new PrincipalAxis(Math.Abs(i1), a1),
                new PrincipalAxis(Math.Abs(i2), a2),
                new PrincipalAxis(Math.Abs(i3), a3)
            };
            axes.Sort((x, y) => x.Moment.CompareTo(y.Moment));

            Vector3d xAxis = NormalizeOrDefault(axes[0].Axis, Vector3d.XAxis);
            Vector3d yAxis = NormalizeOrDefault(axes[1].Axis, Vector3d.YAxis);

            xAxis = OrientAxisByPointCloud(xAxis, vectors);
            yAxis = OrientAxisByPointCloud(yAxis, vectors);
            Vector3d zAxis = Vector3d.CrossProduct(xAxis, yAxis);
            if (!zAxis.Unitize())
                zAxis = NormalizeOrDefault(axes[2].Axis, Vector3d.ZAxis);

            double sum = 0.0;
            double scale = 0.0;
            foreach (Vector3d v in vectors)
            {
                double x = Vector3d.Multiply(v, xAxis);
                double y = Vector3d.Multiply(v, yAxis);
                double z = Vector3d.Multiply(v, zAxis);
                double product = x * y * z;
                sum += product;
                scale += Math.Abs(product);
            }

            Plane plane = new Plane(centroid, xAxis, yAxis);
            double mirrorKey = scale <= Rhino.RhinoMath.ZeroTolerance ? 0.0 : sum / scale;
            return new PrincipalFrame(mirrorKey, scale, plane);
        }

        private List<Vector3d> MeshVectorsFromCentroid(Brep brep, Point3d centroid)
        {
            List<Vector3d> vectors = new List<Vector3d>();
            Mesh[] pieces = Mesh.CreateFromBrep(brep, MeshingParameters.Default);
            if (pieces == null || pieces.Length == 0)
                return vectors;

            foreach (Mesh piece in pieces)
            {
                for (int i = 0; i < piece.Vertices.Count; i++)
                {
                    Vector3d v = piece.Vertices.Point3dAt(i) - centroid;
                    if (v.SquareLength > Rhino.RhinoMath.ZeroTolerance)
                        vectors.Add(v);
                }
            }

            return vectors;
        }

        private Vector3d NormalizeOrDefault(Vector3d axis, Vector3d fallback)
        {
            if (axis.Unitize())
                return axis;

            fallback.Unitize();
            return fallback;
        }

        private Vector3d OrientAxisByPointCloud(Vector3d axis, List<Vector3d> vectors)
        {
            double skew = 0.0;
            double farthest = 0.0;

            foreach (Vector3d v in vectors)
            {
                double projection = Vector3d.Multiply(v, axis);
                skew += projection * projection * projection;
                if (Math.Abs(projection) > Math.Abs(farthest))
                    farthest = projection;
            }

            if (Math.Abs(skew) > Rhino.RhinoMath.ZeroTolerance)
            {
                if (skew < 0)
                    axis.Reverse();
            }
            else if (farthest < 0)
            {
                axis.Reverse();
            }

            return axis;
        }

        private int MirrorSign(double mirrorKey, double tolerance)
        {
            if (Math.Abs(mirrorKey) <= Math.Max(Rhino.RhinoMath.ZeroTolerance, tolerance))
                return 0;
            return mirrorKey > 0 ? 1 : -1;
        }

        private bool IsSameMirror(DbRecord record, FeatureVector feature, int mirrorSign, double mirrorTolerance)
        {
            if (Math.Abs(record.MirrorKey) <= mirrorTolerance || Math.Abs(feature.MirrorKey) <= mirrorTolerance)
                return true;

            return record.MirrorSign == mirrorSign;
        }

        private double MirrorTolerance(List<double> tolerances)
        {
            if (tolerances == null || tolerances.Count == 0)
                return Rhino.RhinoMath.ZeroTolerance;

            return Math.Abs(tolerances[tolerances.Count - 1]);
        }

        private string BuildFeatureCode(FeatureVector feature)
        {
            List<string> parts = new List<string> { "T=" + feature.TextKey };
            if (UseVolume) parts.Add("V=" + FormatDouble(feature.Volume));
            if (UseInertia) parts.Add("I=" + string.Join(",", feature.Inertia.Select(FormatDouble)));
            if (UseArea) parts.Add("A=" + FormatDouble(feature.Area));
            if (UseEdgeLength) parts.Add("E=" + FormatDouble(feature.EdgeLength));
            parts.Add("M=" + FormatDouble(feature.MirrorKey));
            return string.Join("|", parts);
        }

        private bool IsSameBaseFeature(FeatureVector feature, DbRecord record, List<double> tolerances)
        {
            if (!string.Equals(feature.TextKey, record.TextKey, StringComparison.Ordinal))
                return false;

            int toleranceIndex = 0;

            if (UseVolume && !Within(feature.Volume, record.Volume, tolerances, toleranceIndex++))
                return false;

            if (UseInertia)
            {
                double currentInertia = InertiaDistance(feature.Inertia);
                double savedInertia = InertiaDistance(record.Inertia);
                if (!Within(currentInertia, savedInertia, tolerances, toleranceIndex++))
                    return false;
            }

            if (UseArea && !Within(feature.Area, record.Area, tolerances, toleranceIndex++))
                return false;

            if (UseEdgeLength && !Within(feature.EdgeLength, record.EdgeLength, tolerances, toleranceIndex++))
                return false;

            return true;
        }

        private double InertiaDistance(double[] inertia)
        {
            return Math.Sqrt(inertia[0] * inertia[0] + inertia[1] * inertia[1] + inertia[2] * inertia[2]);
        }

        private bool Within(double a, double b, List<double> tolerances, int index)
        {
            double percent = ToleranceAt(tolerances, index);
            double scale = Math.Max(Math.Abs(a), Math.Abs(b));
            if (scale <= Rhino.RhinoMath.ZeroTolerance)
                return Math.Abs(a - b) <= Rhino.RhinoMath.ZeroTolerance;

            return Math.Abs(a - b) <= scale * percent;
        }

        private double ToleranceAt(List<double> tolerances, int index)
        {
            if (tolerances.Count == 1)
                return Math.Abs(tolerances[0]);
            if (index < tolerances.Count)
                return Math.Abs(tolerances[index]);
            return Math.Abs(tolerances[tolerances.Count - 1]);
        }

        private double ToleranceForFeature(int featureIndex)
        {
            if (CurrentTolerances == null || CurrentTolerances.Count == 0)
                return 0.001;

            if (CurrentTolerances.Count == 1)
                return CurrentTolerances[0];

            if (featureIndex < CurrentTolerances.Count)
                return CurrentTolerances[featureIndex];

            return CurrentTolerances[CurrentTolerances.Count - 1];
        }

        private List<double> ParseTolerances(List<object> inputs)
        {
            List<double> tolerances = new List<double>();

            foreach (object input in inputs)
            {
                if (input == null)
                    continue;

                string text = input.ToString().Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                bool hasPercentSign = text.EndsWith("%", StringComparison.Ordinal);
                if (hasPercentSign)
                    text = text.Substring(0, text.Length - 1).Trim();

                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
                    !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                {
                    throw new ArgumentException("Invalid tolerance: " + input);
                }

                double percent = hasPercentSign ? value / 100.0 : value;
                tolerances.Add(Math.Abs(percent));
            }

            if (tolerances.Count == 0)
                tolerances.Add(0.001);

            return tolerances;
        }

        private string FormatPercent(double value)
        {
            return (value * 100.0).ToString("G6", CultureInfo.InvariantCulture) + "%";
        }

        private List<DbRecord> ReadRecords(string dbPath)
        {
            List<DbRecord> records = new List<DbRecord>();
            using (SQLiteConnection connection = BrepCodeDatabase.OpenConnection(dbPath))
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    $"SELECT code, base_code, mirror_sign, feature_code, text_key, volume, inertia1, inertia2, inertia3, area, edge_length, mirror_key FROM {BrepCodeDatabase.TableName}";

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        records.Add(new DbRecord
                        {
                            Code = reader.GetString(0),
                            BaseCode = reader.GetString(1),
                            MirrorSign = reader.GetInt32(2),
                            FeatureCode = reader.GetString(3),
                            TextKey = reader.GetString(4),
                            Volume = reader.GetDouble(5),
                            Inertia = new[] { reader.GetDouble(6), reader.GetDouble(7), reader.GetDouble(8) },
                            Area = reader.GetDouble(9),
                            EdgeLength = reader.GetDouble(10),
                            MirrorKey = reader.GetDouble(11)
                        });
                    }
                }
            }

            return records;
        }

        private int InsertNewRecords(string dbPath, IEnumerable<FeatureResult> results)
        {
            int count = 0;
            using (SQLiteConnection connection = BrepCodeDatabase.OpenConnection(dbPath))
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                foreach (FeatureResult result in results)
                {
                    using (SQLiteCommand command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText =
                            $"INSERT OR IGNORE INTO {BrepCodeDatabase.TableName} " +
                            "(code, base_code, mirror_sign, feature_code, text_key, volume, inertia1, inertia2, inertia3, area, edge_length, mirror_key, created_at, created_by) " +
                            "VALUES (@code, @base_code, @mirror_sign, @feature_code, @text_key, @volume, @inertia1, @inertia2, @inertia3, @area, @edge_length, @mirror_key, @created_at, @created_by)";
                        command.Parameters.AddWithValue("@code", result.Code);
                        command.Parameters.AddWithValue("@base_code", result.BaseCode);
                        command.Parameters.AddWithValue("@mirror_sign", result.MirrorSign);
                        command.Parameters.AddWithValue("@feature_code", result.FeatureCode);
                        command.Parameters.AddWithValue("@text_key", result.Feature.TextKey);
                        command.Parameters.AddWithValue("@volume", result.Feature.Volume);
                        command.Parameters.AddWithValue("@inertia1", result.Feature.Inertia[0]);
                        command.Parameters.AddWithValue("@inertia2", result.Feature.Inertia[1]);
                        command.Parameters.AddWithValue("@inertia3", result.Feature.Inertia[2]);
                        command.Parameters.AddWithValue("@area", result.Feature.Area);
                        command.Parameters.AddWithValue("@edge_length", result.Feature.EdgeLength);
                        command.Parameters.AddWithValue("@mirror_key", result.Feature.MirrorKey);
                        command.Parameters.AddWithValue("@created_at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                        command.Parameters.AddWithValue("@created_by", Environment.MachineName);
                        count += command.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return count;
        }

        private string NextBaseCode(string prefix, int digits, HashSet<string> usedBaseCodes)
        {
            int max = 0;
            foreach (string baseCode in usedBaseCodes)
            {
                if (!baseCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string numberText = baseCode.Substring(prefix.Length);
                if (int.TryParse(numberText, out int number) && number > max)
                    max = number;
            }

            while (true)
            {
                max++;
                string baseCode = prefix + max.ToString(new string('0', digits), CultureInfo.InvariantCulture);
                if (!usedBaseCodes.Contains(baseCode))
                    return baseCode;
            }
        }

        private string NextMirrorSuffix(IEnumerable<string> existingCodes, string baseCode, HashSet<string> usedCodes)
        {
            for (char suffix = 'B'; suffix <= 'Z'; suffix++)
            {
                string code = baseCode + suffix;
                if (!usedCodes.Contains(code) && !existingCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    return suffix.ToString();
            }

            throw new InvalidOperationException("Mirror suffix overflow for " + baseCode + ".");
        }

        private string FormatDouble(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("5F1F4F25-CAD8-49A6-8E9F-49574047EE43");

        private DbRecord ToDbRecord(FeatureResult result)
        {
            return new DbRecord
            {
                Code = result.Code,
                BaseCode = result.BaseCode,
                MirrorSign = result.MirrorSign,
                FeatureCode = result.FeatureCode,
                TextKey = result.Feature.TextKey,
                Volume = result.Feature.Volume,
                Inertia = result.Feature.Inertia,
                Area = result.Feature.Area,
                EdgeLength = result.Feature.EdgeLength,
                MirrorKey = result.Feature.MirrorKey
            };
        }

        private struct PrincipalAxis
        {
            public readonly double Moment;
            public readonly Vector3d Axis;

            public PrincipalAxis(double moment, Vector3d axis)
            {
                Moment = moment;
                Axis = axis;
            }
        }

        private struct PrincipalFrame
        {
            public readonly double MirrorKey;
            public readonly double MirrorScale;
            public readonly Plane MirrorPlane;

            public PrincipalFrame(double mirrorKey, double mirrorScale, Plane mirrorPlane)
            {
                MirrorKey = mirrorKey;
                MirrorScale = mirrorScale;
                MirrorPlane = mirrorPlane;
            }

            public static PrincipalFrame Empty(Point3d origin)
            {
                return new PrincipalFrame(0.0, 0.0, new Plane(origin, Vector3d.XAxis, Vector3d.YAxis));
            }
        }

        private class FeatureVector
        {
            public string TextKey;
            public double Volume;
            public double[] Inertia;
            public double Area;
            public double EdgeLength;
            public double MirrorKey;
            public double MirrorScale;
            public Plane MirrorPlane;
        }

        private class DbRecord
        {
            public string Code;
            public string BaseCode;
            public int MirrorSign;
            public string FeatureCode;
            public string TextKey;
            public double Volume;
            public double[] Inertia;
            public double Area;
            public double EdgeLength;
            public double MirrorKey;
        }

        private class FeatureResult
        {
            public string Code;
            public string BaseCode;
            public int MirrorSign;
            public FeatureVector Feature;
            public string FeatureCode;
            public bool IsNew;
        }
    }

    internal class BrepFeatureCodeAttributes : GH_ComponentAttributes
    {
        public BrepFeatureCodeAttributes(BrepFeatureCode owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 42.0f);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);

            if (channel != GH_CanvasChannel.Objects)
                return;

            BrepFeatureCode component = (BrepFeatureCode)Owner;
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 42, Bounds.Width, 20.0f);
            buttonRect.Inflate(-5.0f, -2.0f);

            using (GH_Capsule capsule = GH_Capsule.CreateCapsule(buttonRect, GH_Palette.Black))
            {
                capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
            }

            using (Font font = new Font(GH_FontServer.Small, FontStyle.Bold))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString("Import", font, Brushes.White, buttonRect, format);
            }

            RectangleF textRect = new RectangleF(Bounds.X + 5, Bounds.Bottom - 21, Bounds.Width - 10, 18.0f);
            using (Font font = new Font(GH_FontServer.Small, FontStyle.Regular))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.DrawString(component.SelectionText, font, Brushes.DimGray, textRect, format);
            }
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            RectangleF buttonRect = new RectangleF(Bounds.X, Bounds.Bottom - 42, Bounds.Width, 20.0f);
            if (e.Button == MouseButtons.Left && buttonRect.Contains(e.CanvasLocation))
            {
                BrepFeatureCode component = (BrepFeatureCode)Owner;
                component.ImportNow = true;
                component.ExpireSolution(true);
                return GH_ObjectResponse.Handled;
            }

            return base.RespondToMouseDown(sender, e);
        }
    }
}
