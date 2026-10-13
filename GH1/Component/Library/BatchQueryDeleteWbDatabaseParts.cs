using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>按编号末尾的纯数字后缀批量查询或删除 WB 分类库零件。</summary>
    public sealed class BatchQueryDeleteWbDatabaseParts : GH_Component
    {
        private bool _deleteRequested;
        private readonly List<string> _lastNumbers = new List<string>();

        public BatchQueryDeleteWbDatabaseParts()
            : base("批量查询删除分类库（WB）", "批量查询删除_WB",
                "按编号前缀正则表达式和/或末尾纯数字后缀范围查询 WB 分类库；点击底部“删除查询结果”可批量删除当前查询结果。",
                "Parrot", "Library")
        {
        }

        public override Guid ComponentGuid => new Guid("4BDBD725-8D06-46A1-96F4-7EF00A360C65");

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager p)
        {
            p.AddTextParameter("数据库地址", "数据库", "WB 分类库 SQLite 文件地址。", GH_ParamAccess.item);
            p.AddTextParameter("前缀（正则）", "前缀", "编号前缀正则表达式；从编号开头匹配。留空则不限制前缀。", GH_ParamAccess.item, string.Empty);
            p.AddIntegerParameter("后缀起点", "起点", "编号末尾纯数字后缀的起点，包含此值。", GH_ParamAccess.item);
            p.AddIntegerParameter("后缀终点", "终点", "编号末尾纯数字后缀的终点，包含此值。", GH_ParamAccess.item);
            p[1].Optional = true;
            p[2].Optional = true;
            p[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager p)
        {
            p.AddTextParameter("满足条件的编号", "编号", "同时满足前缀正则表达式和后缀范围（已填写时）的全部编号。", GH_ParamAccess.list);
        }

        public override void CreateAttributes()
        {
            m_attributes = new BatchQueryDeleteWbDatabasePartsAttributes(this);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            string database = string.Empty;
            string prefixPattern = string.Empty;
            int start = -1;
            int end = -1;
            if (!da.GetData(0, ref database))
                return;
            da.GetData(1, ref prefixPattern);
            da.GetData(2, ref start);
            da.GetData(3, ref end);

            try
            {
                ValidateInputs(database, prefixPattern, start, end);
                _lastNumbers.Clear();
                _lastNumbers.AddRange(QueryPartNumbers(database, prefixPattern, start, end));

                if (_deleteRequested)
                {
                    _deleteRequested = false;
                    DeleteCurrentResults(database, prefixPattern, start, end);
                    _lastNumbers.Clear();
                    _lastNumbers.AddRange(QueryPartNumbers(database, prefixPattern, start, end));
                }
            }
            catch (Exception ex)
            {
                _deleteRequested = false;
                _lastNumbers.Clear();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
            }

            da.SetDataList(0, _lastNumbers);
        }

        internal void RequestDelete()
        {
            _deleteRequested = true;
            ExpireSolution(true);
        }

        private void DeleteCurrentResults(string database, string prefixPattern, int start, int end)
        {
            // 点击删除时再次读取，避免删除已经过时的查询缓存。
            List<string> numbers = QueryPartNumbers(database, prefixPattern, start, end);
            if (numbers.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "当前范围没有可删除的编号。");
                return;
            }

            string preview = string.Join("\n", numbers.Take(20));
            if (numbers.Count > 20)
                preview += "\n……等共 " + numbers.Count.ToString(CultureInfo.InvariantCulture) + " 个编号";

            DialogResult confirmation = MessageBox.Show(
                "确定要删除当前查询到的 " + numbers.Count.ToString(CultureInfo.InvariantCulture) + " 个编号吗？\n\n" +
                preview + "\n\n将同时删除零件、孔、外轮廓及完整特征记录，此操作不可撤销。",
                "确认批量删除分类库数据（WB）",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "已取消批量删除。");
                return;
            }

            using (var connection = new SQLiteConnection("Data Source=" + Path.GetFullPath(database) + ";Version=3;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                using (var transaction = connection.BeginTransaction())
                {
                    command.Transaction = transaction;
                    foreach (string number in numbers)
                    {
                        command.CommandText = "SELECT part_id FROM parts WHERE part_number=@number COLLATE NOCASE;";
                        command.Parameters.Clear();
                        command.Parameters.AddWithValue("@number", number);
                        object value = command.ExecuteScalar();
                        if (value == null || value == DBNull.Value)
                            throw new InvalidOperationException("删除前编号已不存在，整批未删除：" + number);

                        long partId = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                        DeleteByPartId(command, "holes", partId);
                        DeleteByPartId(command, "outer_profiles", partId);
                        DeleteByPartId(command, "part_features", partId);
                        DeleteByPartId(command, "parts", partId);
                    }
                    transaction.Commit();
                }
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                "已删除 " + numbers.Count.ToString(CultureInfo.InvariantCulture) + " 个编号。");
        }

        private static List<string> QueryPartNumbers(string database, string prefixPattern, int start, int end)
        {
            var result = new List<string>();
            Regex prefixRegex = CreatePrefixRegex(prefixPattern);
            bool hasSuffixRange = start >= 0 && end >= 0;
            using (var connection = new SQLiteConnection("Data Source=" + Path.GetFullPath(database) + ";Version=3;Read Only=True;"))
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT part_number FROM parts ORDER BY part_number COLLATE NOCASE;";
                    using (SQLiteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string number = reader.GetString(0);
                            if (!prefixRegex.IsMatch(number)) continue;
                            if (!hasSuffixRange ||
                                (TryParseTrailingSequence(number, out int sequence) &&
                                 sequence >= start && sequence <= end))
                                result.Add(number);
                        }
                    }
                }
            }
            return result;
        }

        private static bool TryParseTrailingSequence(string number, out int sequence)
        {
            sequence = -1;
            if (string.IsNullOrWhiteSpace(number)) return false;

            int firstDigit = number.Length;
            while (firstDigit > 0 && char.IsDigit(number[firstDigit - 1])) firstDigit--;
            return firstDigit < number.Length &&
                int.TryParse(number.Substring(firstDigit), NumberStyles.None,
                    CultureInfo.InvariantCulture, out sequence);
        }

        private static void ValidateInputs(string database, string prefixPattern, int start, int end)
        {
            if (string.IsNullOrWhiteSpace(database))
                throw new ArgumentException("数据库地址不能为空。");
            if (!File.Exists(database))
                throw new FileNotFoundException("数据库文件不存在：" + database);
            CreatePrefixRegex(prefixPattern);
            if ((start < 0) != (end < 0))
                throw new ArgumentException("后缀起点和终点应同时填写，或同时留空。");
            if (start >= 0 && start > end)
                throw new ArgumentException("后缀起点不能大于终点。");
            if (!WbMigrationStore.TryValidateWbDatabase(database, out string message))
                throw new ArgumentException(message);
        }

        private static Regex CreatePrefixRegex(string prefixPattern)
        {
            try
            {
                // 外层锚定确保普通前缀和正则表达式均从编号开头匹配。
                return new Regex("^(?:" + (prefixPattern ?? string.Empty) + ")",
                    RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("前缀正则表达式无效：" + ex.Message, ex);
            }
        }

        private static void DeleteByPartId(SQLiteCommand command, string tableName, long partId)
        {
            command.CommandText = "DELETE FROM " + tableName + " WHERE part_id=@partId;";
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@partId", partId);
            command.ExecuteNonQuery();
        }
    }

    internal sealed class BatchQueryDeleteWbDatabasePartsAttributes : GH_ComponentAttributes
    {
        private const int ButtonHeight = 24;

        public BatchQueryDeleteWbDatabasePartsAttributes(BatchQueryDeleteWbDatabaseParts owner) : base(owner)
        {
        }

        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + ButtonHeight + 4);
        }

        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;

            RectangleF button = GetDeleteButtonBounds();
            GH_Capsule capsule = GH_Capsule.CreateTextCapsule(button, button, GH_Palette.Black,
                "删除查询结果", GH_FontServer.Small);
            capsule.Render(graphics, Selected, Owner.Locked, false);
            capsule.Dispose();
        }

        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (Owner.Locked || Owner.Hidden)
                return base.RespondToMouseDown(sender, e);
            if (e.Button == MouseButtons.Left && GetDeleteButtonBounds().Contains(e.CanvasLocation))
            {
                ((BatchQueryDeleteWbDatabaseParts)Owner).RequestDelete();
                return GH_ObjectResponse.Handled;
            }
            return base.RespondToMouseDown(sender, e);
        }

        private RectangleF GetDeleteButtonBounds()
        {
            return new RectangleF(Bounds.X + 2, Bounds.Bottom - ButtonHeight - 2,
                Bounds.Width - 4, ButtonHeight);
        }
    }
}
