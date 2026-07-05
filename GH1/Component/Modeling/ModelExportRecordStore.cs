using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace NS_Parrot
{
    internal sealed class ModelExportRecordStore
    {
        private const string TableName = "model_export_records";
        private readonly string _databasePath;

        public ModelExportRecordStore(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("数据库地址不能为空。");

            _databasePath = Path.GetFullPath(databasePath.Trim());
        }

        public void EnsureSchema()
        {
            string folder = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            if (!File.Exists(_databasePath))
                SQLiteConnection.CreateFile(_databasePath);

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    @"CREATE TABLE IF NOT EXISTS model_export_records (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        source_rhino_path TEXT NOT NULL,
                        model_code TEXT NOT NULL,
                        object_guid TEXT NOT NULL DEFAULT '',
                        export_path TEXT NOT NULL,
                        export_format TEXT NOT NULL,
                        export_event_id TEXT NOT NULL DEFAULT '',
                        exported_at TEXT NOT NULL,
                        file_size INTEGER NOT NULL DEFAULT 0
                    )";
                command.ExecuteNonQuery();

                EnsureColumn(connection, "export_event_id", "TEXT NOT NULL DEFAULT ''");

                command.CommandText =
                    @"CREATE INDEX IF NOT EXISTS idx_model_export_records_duplicate
                      ON model_export_records(source_rhino_path, model_code, export_event_id, exported_at)";
                command.ExecuteNonQuery();
            }
        }

        private static void EnsureColumn(SQLiteConnection connection, string columnName, string definition)
        {
            HashSet<string> columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(" + TableName + ")";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        columns.Add(reader.GetString(1));
                }
            }

            if (columns.Contains(columnName))
                return;

            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "ALTER TABLE " + TableName + " ADD COLUMN " + columnName + " " + definition;
                command.ExecuteNonQuery();
            }
        }

        public void AddRecord(ModelExportRecord record)
        {
            EnsureSchema();

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    @"INSERT INTO model_export_records
                      (source_rhino_path, model_code, object_guid, export_path, export_format, export_event_id, exported_at, file_size)
                      VALUES
                      (@source_rhino_path, @model_code, @object_guid, @export_path, @export_format, @export_event_id, @exported_at, @file_size)";
                command.Parameters.AddWithValue("@source_rhino_path", record.SourceRhinoPath ?? "");
                command.Parameters.AddWithValue("@model_code", record.ModelCode ?? "");
                command.Parameters.AddWithValue("@object_guid", record.ObjectGuid ?? "");
                command.Parameters.AddWithValue("@export_path", record.ExportPath ?? "");
                command.Parameters.AddWithValue("@export_format", record.ExportFormat ?? "");
                command.Parameters.AddWithValue("@export_event_id", record.ExportEventId ?? "");
                command.Parameters.AddWithValue("@exported_at", record.ExportedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                command.Parameters.AddWithValue("@file_size", record.FileSize);
                command.ExecuteNonQuery();
            }
        }

        public List<DuplicateModelExportRecord> QueryDuplicates(string sourceRhinoFilter, int minimumCount)
        {
            EnsureSchema();

            List<DuplicateModelExportRecord> records = new List<DuplicateModelExportRecord>();
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                bool hasSourceFilter = !string.IsNullOrWhiteSpace(sourceRhinoFilter);
                command.CommandText =
                    @"SELECT
                        grouped.source_rhino_path,
                        grouped.model_code,
                        COUNT(*) AS export_count,
                        MIN(grouped.first_exported_at) AS first_exported_at,
                        MAX(grouped.last_exported_at) AS last_exported_at
                      FROM (
                        SELECT
                            source_rhino_path,
                            model_code,
                            CASE WHEN export_event_id IS NULL OR export_event_id = '' THEN CAST(id AS TEXT) ELSE export_event_id END AS event_key,
                            MIN(exported_at) AS first_exported_at,
                            MAX(exported_at) AS last_exported_at
                        FROM model_export_records
                        WHERE (@has_source_filter = 0 OR source_rhino_path = @source_rhino_path)
                        GROUP BY source_rhino_path, model_code, event_key
                      ) grouped
                      GROUP BY grouped.source_rhino_path, grouped.model_code
                      HAVING COUNT(*) >= @minimum_count
                      ORDER BY last_exported_at DESC, grouped.source_rhino_path, grouped.model_code";
                command.Parameters.AddWithValue("@has_source_filter", hasSourceFilter ? 1 : 0);
                command.Parameters.AddWithValue("@source_rhino_path", NormalizePath(sourceRhinoFilter));
                command.Parameters.AddWithValue("@minimum_count", Math.Max(2, minimumCount));

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        DuplicateModelExportRecord record = new DuplicateModelExportRecord
                        {
                            SourceRhinoPath = reader.GetString(0),
                            ModelCode = reader.GetString(1),
                            ExportCount = Convert.ToInt32(reader.GetValue(2)),
                            FirstExportedAt = reader.GetString(3),
                            LastExportedAt = reader.GetString(4)
                        };
                        records.Add(record);
                    }
                }
            }

            foreach (DuplicateModelExportRecord record in records)
                FillPaths(connectionPath: null, record);

            return records;
        }

        private void FillPaths(string connectionPath, DuplicateModelExportRecord record)
        {
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    @"SELECT export_path
                      FROM model_export_records
                      WHERE source_rhino_path = @source_rhino_path
                        AND model_code = @model_code
                      ORDER BY exported_at ASC, id ASC";
                command.Parameters.AddWithValue("@source_rhino_path", record.SourceRhinoPath);
                command.Parameters.AddWithValue("@model_code", record.ModelCode);

                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        record.ExportPaths.Add(reader.GetString(0));
                }
            }

            if (record.ExportPaths.Count > 0)
                record.LastExportPath = record.ExportPaths[record.ExportPaths.Count - 1];
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = new SQLiteConnection("Data Source=" + _databasePath + ";Version=3;");
            connection.Open();
            return connection;
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            try
            {
                return Path.GetFullPath(path.Trim());
            }
            catch
            {
                return path.Trim();
            }
        }
    }

    internal sealed class ModelExportRecord
    {
        public string SourceRhinoPath { get; set; }
        public string ModelCode { get; set; }
        public string ObjectGuid { get; set; }
        public string ExportPath { get; set; }
        public string ExportFormat { get; set; }
        public string ExportEventId { get; set; }
        public DateTime ExportedAt { get; set; }
        public long FileSize { get; set; }
    }

    internal sealed class DuplicateModelExportRecord
    {
        public string SourceRhinoPath { get; set; }
        public string ModelCode { get; set; }
        public int ExportCount { get; set; }
        public string FirstExportedAt { get; set; }
        public string LastExportedAt { get; set; }
        public string LastExportPath { get; set; }
        public List<string> ExportPaths { get; } = new List<string>();

        public string AllExportPaths
        {
            get { return string.Join(" ; ", ExportPaths); }
        }

        public string DetailText
        {
            get
            {
                return $"{ModelCode} | {ExportCount}次 | 首次: {FirstExportedAt} | 最后: {LastExportedAt} | {LastExportPath}";
            }
        }
    }
}
