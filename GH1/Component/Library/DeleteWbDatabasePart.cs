using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>按编号删除 WB 分类数据库中的单个零件及其关联特征。</summary>
    public sealed class DeleteWbDatabasePart : GH_Component
    {
        private bool _lastExecute;
        private bool _lastDeleted;
        private string _lastMessage = string.Empty;

        public DeleteWbDatabasePart()
            : base("删除分类库实体（WB）", "删除实体_WB",
                "按编号删除 WB 分类数据库中的零件、孔、外轮廓和完整特征。执行端仅在上升沿删除。",
                "Parrot", "Library") { }

        public override Guid ComponentGuid => new Guid("6B1C6F48-7D1E-4A9B-8B17-2F83D4A6E901");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("数据库地址", "数据库", "WB 分类库 SQLite 文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("要删除的编号", "编号", "要删除的完整零件编号，可输入列表。", GH_ParamAccess.list);
            p.AddBooleanParameter("执行", "执行", "False→True 时执行删除全部编号。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddBooleanParameter("已删除", "删除", "本次是否成功删除指定编号。", GH_ParamAccess.item);
            p.AddTextParameter("结果", "结果", "删除结果或错误信息。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            var numbers = new List<string>();
            bool execute = false;
            da.GetData(0, ref database);
            da.GetDataList(1, numbers);
            da.GetData(2, ref execute);

            bool rising = execute && !_lastExecute;
            _lastExecute = execute;
            if (!rising)
            {
                da.SetData(0, _lastDeleted);
                da.SetData(1, _lastMessage);
                return;
            }

            _lastDeleted = false;
            try
            {
                if (string.IsNullOrWhiteSpace(database))
                    throw new ArgumentException("数据库地址不能为空。");
                if (!File.Exists(database))
                    throw new FileNotFoundException("数据库文件不存在：" + database);
                var uniqueNumbers = new List<string>();
                foreach (string item in numbers)
                {
                    string number = item == null ? string.Empty : item.Trim();
                    if (string.IsNullOrWhiteSpace(number)) continue;
                    if (!uniqueNumbers.Exists(x => string.Equals(x, number, StringComparison.OrdinalIgnoreCase)))
                        uniqueNumbers.Add(number);
                }
                if (uniqueNumbers.Count == 0)
                    throw new ArgumentException("要删除的编号不能为空。");

                DialogResult confirmation = MessageBox.Show(
                    "确定要从分类数据库中删除 " + uniqueNumbers.Count + " 个编号吗？\n\n" +
                    string.Join("\n", uniqueNumbers) + "\n\n" +
                    "将同时删除这些实体的孔、外轮廓及完整特征记录，此操作不可撤销。",
                    "确认删除实体（WB）",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    _lastMessage = "已取消删除 " + uniqueNumbers.Count + " 个编号。";
                    da.SetData(0, false);
                    da.SetData(1, _lastMessage);
                    return;
                }

                using (var connection = new SQLiteConnection("Data Source=" + Path.GetFullPath(database) + ";Version=3;"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    using (var transaction = connection.BeginTransaction())
                    {
                        command.Transaction = transaction;
                        var partIds = new List<long>();
                        var missingNumbers = new List<string>();
                        foreach (string number in uniqueNumbers)
                        {
                            command.CommandText = "SELECT part_id FROM parts WHERE part_number=@number COLLATE NOCASE;";
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@number", number);
                            object value = command.ExecuteScalar();
                            if (value == null || value == DBNull.Value)
                                missingNumbers.Add(number);
                            else
                                partIds.Add(Convert.ToInt64(value));
                        }

                        if (missingNumbers.Count > 0)
                            throw new InvalidOperationException("未找到编号" +
                                string.Join("、", missingNumbers) + "，整批未删除。");

                        foreach (long partId in partIds)
                        {
                            DeleteByPartId(command, "holes", partId);
                            DeleteByPartId(command, "outer_profiles", partId);
                            DeleteByPartId(command, "part_features", partId);
                            DeleteByPartId(command, "parts", partId);
                        }
                        transaction.Commit();

                        _lastDeleted = true;
                        _lastMessage = "已删除 " + uniqueNumbers.Count + " 个编号：" + string.Join("、", uniqueNumbers);
                    }
                }
            }
            catch (Exception ex)
            {
                _lastMessage = "删除失败：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastMessage);
            }

            da.SetData(0, _lastDeleted);
            da.SetData(1, _lastMessage);
        }

        private static void DeleteByPartId(SQLiteCommand command, string tableName, long partId)
        {
            command.CommandText = "DELETE FROM " + tableName + " WHERE part_id=@partId;";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@partId", partId);
            command.ExecuteNonQuery();
        }
    }
}
