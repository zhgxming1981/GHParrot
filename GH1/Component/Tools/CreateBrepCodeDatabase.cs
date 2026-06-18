using CommonFunction;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class CreateBrepCodeDatabase : GH_Component
    {
        private const string TableName = "entity_feature_classes";

        public bool RunNow { get; set; }
        private string _lastPath = string.Empty;

        public CreateBrepCodeDatabase()
          : base("CreateBrepCodeDatabase", "CreateBrepDB",
              "创建 EntityFeatureClassifier 使用的 SQLite 数据库",
              "Parrot", "Tools")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("文件夹", "文件夹", "数据库文件夹路径", GH_ParamAccess.item);
            pManager.AddTextParameter("数据库名称", "数据库名称", "数据库文件名", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("数据库地址", "数据库地址", "创建后的数据库完整路径", GH_ParamAccess.item);
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
                string fullPath = CreateDatabase(folder, databaseName);
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

        private static string CreateDatabase(string folder, string databaseName)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("数据库文件夹不能为空。");
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new ArgumentException("数据库名称不能为空。");

            string fullFolder = Path.GetFullPath(folder);
            Directory.CreateDirectory(fullFolder);

            string fileName = databaseName.Trim();
            if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("数据库名称必须是文件名，不能是路径。");

            if (string.IsNullOrWhiteSpace(Path.GetExtension(fileName)))
                fileName += ".db";

            string fullPath = Path.GetFullPath(Path.Combine(fullFolder, fileName));
            if (!File.Exists(fullPath))
                SQLiteConnection.CreateFile(fullPath);

            EnsureSchema(fullPath);
            return fullPath;
        }

        private static SQLiteConnection OpenConnection(string dbPath)
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
                    $@"CREATE TABLE IF NOT EXISTS {TableName} (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        prefix_text TEXT NOT NULL DEFAULT '',
                        material_text TEXT NOT NULL,
                        color_text TEXT NOT NULL,
                        other_text TEXT NOT NULL,
                        base_number INTEGER NOT NULL,
                        area REAL NOT NULL,
                        volume REAL NOT NULL,
                        edge_sum REAL NOT NULL,
                        inertia_1 REAL NOT NULL,
                        inertia_2 REAL NOT NULL,
                        inertia_3 REAL NOT NULL,
                        hole_data TEXT NOT NULL DEFAULT '',
                        mirror_code TEXT NOT NULL,
                        mirror_score REAL NOT NULL,
                        hole_mirror_score REAL NOT NULL DEFAULT 0,
                        feature_version INTEGER NOT NULL,
                        created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
                    )";
                command.ExecuteNonQuery();

                command.CommandText =
                    $@"CREATE INDEX IF NOT EXISTS idx_entity_feature_classes_text_number
                       ON {TableName}(prefix_text, material_text, color_text, other_text, base_number)";
                command.ExecuteNonQuery();
            }

            EnsureColumn(dbPath, "hole_mirror_score", "REAL NOT NULL DEFAULT 0");
            ValidateSchema(dbPath);
        }

        private static void EnsureColumn(string dbPath, string columnName, string definition)
        {
            HashSet<string> columns = ReadColumnNames(dbPath);
            if (columns.Contains(columnName))
                return;

            using (SQLiteConnection connection = OpenConnection(dbPath))
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE " + TableName + " ADD COLUMN " + columnName + " " + definition + ";";
                command.ExecuteNonQuery();
            }
        }

        private static void ValidateSchema(string dbPath)
        {
            HashSet<string> columns = ReadColumnNames(dbPath);

            string[] requiredColumns =
            {
                "prefix_text",
                "material_text",
                "color_text",
                "other_text",
                "base_number",
                "area",
                "volume",
                "edge_sum",
                "inertia_1",
                "inertia_2",
                "inertia_3",
                "hole_data",
                "mirror_code",
                "mirror_score",
                "hole_mirror_score",
                "feature_version"
            };

            if (columns.Contains("model_text"))
                throw new InvalidOperationException("数据库表 " + TableName + " 含有旧字段 model_text，请删除旧数据库或更换数据库名称后重建。");

            foreach (string column in requiredColumns)
            {
                if (!columns.Contains(column))
                    throw new InvalidOperationException("数据库表 " + TableName + " 不是新版结构，请使用新版数据库或重建该表。");
            }
        }

        private static HashSet<string> ReadColumnNames(string dbPath)
        {
            HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteConnection connection = OpenConnection(dbPath))
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + TableName + ")";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader.GetString(1));
                    }
                }
            }

            return columns;
        }

        protected override Bitmap Icon => GeneratedIcon.Get("gen_CreateBrepCodeDatabase");

        public override Guid ComponentGuid => new Guid("1C99A7F9-E56F-4EC3-9D32-5E0DA15B3C35");
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
                graphics.DrawString("运行", font, Brushes.White, buttonRect, format);
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
}
