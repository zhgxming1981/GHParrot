using Grasshopper.Kernel;
using System;
using System.Data.SQLite;
using System.IO;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>安全替换 WB 分类数据库中的实体编号。</summary>
    public sealed class ReplaceWbDatabasePartNumber : GH_Component
    {
        private bool _lastExecute;
        private bool _lastReplaced;
        private string _lastMessage = string.Empty;

        public ReplaceWbDatabasePartNumber()
            : base("替换数据库编号（WB）", "替换编号_WB",
                "将 WB 分类库中一个实体的编号替换为新编号。执行前会要求确认。",
                "Parrot", "Library") { }

        public override Guid ComponentGuid => new Guid("9C3E571A-40D2-4B97-91AB-5A03D8E7C56F");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("数据库地址", "数据库", "WB 分类库 SQLite 文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("原编号", "原编号", "数据库中现有的完整编号。", GH_ParamAccess.item);
            p.AddTextParameter("替换为", "新编号", "替换后的完整编号，不能为空且不能与已有编号重复。", GH_ParamAccess.item);
            p.AddBooleanParameter("执行", "执行", "False→True 时弹出确认并执行替换。", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddBooleanParameter("已替换", "替换", "本次是否已成功替换编号。", GH_ParamAccess.item);
            p.AddTextParameter("结果", "结果", "替换结果、取消信息或错误信息。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            string oldNumber = string.Empty;
            string newNumber = string.Empty;
            bool execute = false;
            da.GetData(0, ref database);
            da.GetData(1, ref oldNumber);
            da.GetData(2, ref newNumber);
            da.GetData(3, ref execute);

            bool rising = execute && !_lastExecute;
            _lastExecute = execute;
            if (!rising)
            {
                da.SetData(0, _lastReplaced);
                da.SetData(1, _lastMessage);
                return;
            }

            _lastReplaced = false;
            try
            {
                database = database == null ? string.Empty : database.Trim();
                oldNumber = oldNumber == null ? string.Empty : oldNumber.Trim();
                newNumber = newNumber == null ? string.Empty : newNumber.Trim();
                if (string.IsNullOrWhiteSpace(database)) throw new ArgumentException("数据库地址不能为空。");
                if (!File.Exists(database)) throw new FileNotFoundException("数据库文件不存在：" + database);
                if (string.IsNullOrWhiteSpace(oldNumber)) throw new ArgumentException("原编号不能为空。");
                if (string.IsNullOrWhiteSpace(newNumber)) throw new ArgumentException("新编号不能为空。");
                if (string.Equals(oldNumber, newNumber, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("原编号与新编号相同，无需替换。");

                DialogResult confirmation = MessageBox.Show(
                    "确定将数据库编号\n\n“" + oldNumber + "”\n\n替换为\n\n“" + newNumber + "”吗？\n\n" +
                    "这会直接修改分类库记录，确认后不可自动撤销。",
                    "确认替换数据库编号（WB）",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);
                if (confirmation != DialogResult.Yes)
                {
                    _lastMessage = "已取消替换：" + oldNumber + " → " + newNumber;
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
                        command.CommandText = "SELECT COUNT(*) FROM parts WHERE part_number=@newNumber COLLATE NOCASE;";
                        command.Parameters.AddWithValue("@newNumber", newNumber);
                        if (Convert.ToInt64(command.ExecuteScalar()) > 0)
                            throw new InvalidOperationException("新编号已存在，未做替换：" + newNumber);

                        command.CommandText = "UPDATE parts SET part_number=@newNumber WHERE part_number=@oldNumber COLLATE NOCASE;";
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@newNumber", newNumber);
                        command.Parameters.AddWithValue("@oldNumber", oldNumber);
                        int affected = command.ExecuteNonQuery();
                        if (affected != 1)
                            throw new InvalidOperationException(affected == 0
                                ? "未找到原编号：" + oldNumber
                                : "原编号命中多条记录，已回滚，未做替换。");

                        transaction.Commit();
                    }
                }

                _lastReplaced = true;
                _lastMessage = "已替换编号：" + oldNumber + " → " + newNumber;
            }
            catch (Exception ex)
            {
                _lastMessage = "替换失败：" + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastMessage);
            }

            da.SetData(0, _lastReplaced);
            da.SetData(1, _lastMessage);
        }
    }
}
