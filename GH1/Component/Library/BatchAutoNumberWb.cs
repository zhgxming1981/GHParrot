using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Attributes;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.FileIO;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NS_Parrot
{
    /// <summary>
    /// 只负责多文件调度及编号落地。分类、编号分配、数据库参数和右键误差菜单
    /// 全部继承实体分类_WB，避免维护第二套匹配算法。
    /// 文件操作在 GH 解算结束后执行；每个文件成功保存后才推进队列。
    /// </summary>
    public sealed class BatchAutoNumberWb : WbEntityClassifier
    {
        private enum Stage { Idle, Opening, Waiting, Processing, Publishing, ResettingPulse, Finishing, Paused, Complete }
        private Stage _stage;
        private readonly List<string> _files = new List<string>();
        private readonly List<string> _fileStatuses = new List<string>();
        private readonly GH_Structure<GH_Guid> _ids = new GH_Structure<GH_Guid>();
        private readonly GH_Structure<GH_String> _numbers = new GH_Structure<GH_String>();
        private readonly GH_Structure<GH_String> _matches = new GH_Structure<GH_String>();
        private int _index;
        private int _generation;
        private int _checks;
        private int _stableCount;
        private string _signature;
        private string _batchDatabase;
        private string _summary = "未执行：点击运行开始批量编号。";
        private bool _lastRun;
        private bool _requested;
        private bool _pulse;
        private Timer _timer;
        private Timer _refreshTimer;
        private bool _refreshUpstream;
        private bool _executingAction;
        private int _solveCount;
        private GH_Document _completionDocument;
        private uint _completedDocumentSerial;

        public BatchAutoNumberWb() : base("批量自动编号（WB）", "批量自动编号_WB",
            "依次打开 Rhino 文件，使用实体分类_WB 的共享算法编号，写入零件编号用户字符串及对象名称，并保存原文件。", false) { }

        public override Guid ComponentGuid => new Guid("BC84F5D0-B9B3-4B1B-8AE2-5A2990C46175");

        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("文件路径", "文件路径", "按顺序处理的 .3dm 文件；为空时处理当前已保存的文档。", GH_ParamAccess.list);
            p.AddGenericParameter("Guid", "Guid", "当前文件待编号对象；连接随当前文档刷新的上游筛选电池。", GH_ParamAccess.list);
            p.AddTextParameter("材质", "材质", "可空、单值统一使用，或与 Guid 一一对应。", GH_ParamAccess.list);
            p.AddTextParameter("颜色", "颜色", "可空、单值统一使用，或与 Guid 一一对应。", GH_ParamAccess.list);
            p.AddTextParameter("数据库地址", "数据库", "整批共用的 WB 分类数据库。", GH_ParamAccess.item);
            p.AddTextParameter("编号前缀", "前缀", "可空、单值统一使用，或与 Guid 一一对应。", GH_ParamAccess.list);
            p.AddIntegerParameter("数字长度", "位数", "新编号数字部分的最小位数。", GH_ParamAccess.item, 4);
            p.AddTextParameter("连字符", "连字符", "前缀和数字的连接符，允许为空。", GH_ParamAccess.item, "-");
            p.AddIntegerParameter("起始序号", "起始", "结合数据库已有编号续编。", GH_ParamAccess.item, 1);
            p.AddBooleanParameter("运行", "运行", "上升沿启动整批任务。", GH_ParamAccess.item, false);
            // 缺少输入也要进入解算，以便批处理报告原因并允许停止、继续。
            for (int i = 0; i < p.ParamCount; i++) p[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("文件路径", "文件路径", "当前处理文件的路径。", GH_ParamAccess.list);
            p.AddGenericParameter("Guid", "Guid", "仅当前文件的对象，固定输出到 {0} 分支，不累计。", GH_ParamAccess.tree);
            p.AddTextParameter("编号", "编号", "与 Guid 一一对应；保存是否成功请查看文件状态。", GH_ParamAccess.tree);
            p.AddTextParameter("命中状态", "命中状态", "共享 WB 分类算法返回的逐件结果。", GH_ParamAccess.tree);
            p.AddTextParameter("文件状态", "文件状态", "当前文件分类、入库、写回及保存结果。", GH_ParamAccess.list);
            p.AddTextParameter("汇总状态", "汇总", "批次进度或暂停原因。", GH_ParamAccess.item);
            p.AddBooleanParameter("完成脉冲", "完成", "每个文件编号成功后输出一轮 True，供下游保存编号；下游本轮解算及脉冲复位完成后才切换文件。", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess da)
        {
            // 批处理只能有一个队列，不能由 GH 的数据树匹配隐式启动多个队列。
            if (da.Iteration > 0)
            {
                return;
            }
            _solveCount++;
            bool run = false;
            da.GetData(9, ref run);
            bool start = _requested || (run && !_lastRun);
            _requested = false;
            _lastRun = run;
            try
            {
                if ((start || _stage == Stage.Waiting) && Params.Input.Any(param => param.VolatileData.PathCount > 1))
                    throw new InvalidOperationException("批量输入请先展平，不能按多个分支重复执行。");
                if (start && (_stage == Stage.Idle || _stage == Stage.Complete || _stage == Stage.Paused))
                    Start(da);
                if (_stage == Stage.Waiting)
                    CheckInputs(da);
            }
            catch (Exception ex) { Pause(ex.Message); }

            // GH 每次解算会清除运行消息；暂停原因必须持续显示，不能只报一次。
            if (_stage == Stage.Paused)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _summary);

            int currentIndex = Math.Min(_index, _files.Count - 1);
            da.SetDataList(0, currentIndex < 0 ? Array.Empty<string>() : new[] { _files[currentIndex] });
            da.SetDataTree(1, _ids);
            da.SetDataTree(2, _numbers);
            da.SetDataTree(3, _matches);
            da.SetDataList(4, currentIndex < 0 ? Array.Empty<string>() : new[] { _fileStatuses[currentIndex] });
            da.SetData(5, _summary);
            da.SetData(6, _pulse);
            // 必须在输出后监听本轮结束，不能在 SolveInstance 内立即打开下一文件。
            // True 和复位 False 均保留相同 Guid/编号，让下游的上升沿检测可靠复位。
            if (_stage == Stage.Publishing || _stage == Stage.ResettingPulse)
                WaitForDownstream();
        }

        private void Start(IGH_DataAccess da)
        {
            var files = new List<string>();
            da.GetDataList(0, files);
            // 空面板可能提供空字符串而不是空列表；先移除空项，再决定是否使用当前文档。
            files = files.Where(path => !string.IsNullOrWhiteSpace(path?.Trim().Trim('"')))
                .Select(NormalizePath).ToList();
            if (files.Count == 0)
            {
                RhinoDoc current = RhinoDoc.ActiveDoc;
                if (current == null)
                    throw new InvalidOperationException("文件路径为空，且没有活动的 Rhino 文档。");
                if (string.IsNullOrWhiteSpace(current.Path))
                    throw new InvalidOperationException("当前 Rhino 文档尚未保存，请先指定保存路径，再运行编号。");
                files.Add(NormalizePath(current.Path));
            }
            if (files.Any(path => !File.Exists(path) || !string.Equals(Path.GetExtension(path), ".3dm", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("文件路径必须是已存在的 .3dm 文件；当前文档模式也必须先有保存路径。");
            if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count)
                throw new InvalidOperationException("文件路径存在重复，请先去重。");
            string database = "";
            da.GetData(4, ref database);
            if (!WbMigrationStore.TryValidateWbDatabase(NormalizePath(database), out string error))
                throw new InvalidOperationException(error);

            CancelPending();
            _files.Clear();
            _files.AddRange(files);
            _batchDatabase = NormalizePath(database);
            _fileStatuses.Clear();
            _fileStatuses.AddRange(files.Select(_ => "待处理"));
            _ids.Clear(); _numbers.Clear(); _matches.Clear();
            _index = 0;
            _pulse = false;
            OpenCurrent();
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path.Trim().Trim('"'));
        }

        private bool IsCurrent(RhinoDoc doc)
        {
            return doc != null && _index < _files.Count &&
                string.Equals(NormalizePath(doc.Path), _files[_index], StringComparison.OrdinalIgnoreCase);
        }

        private void OpenCurrent()
        {
            Message = null;
            _ids.Clear(); _numbers.Clear(); _matches.Clear();
            _pulse = false;
            _stage = Stage.Opening;
            _summary = $"正在打开 {_index + 1}/{_files.Count}：{_files[_index]}";
            Defer(() =>
            {
                RhinoDoc current = RhinoDoc.ActiveDoc;
                if (!IsCurrent(current))
                {
                    // 不复用导出电池清除 Modified 标记的做法：切换前保存真实改动。
                    if (current != null && current.Modified) SaveDocument(current);
                    if (_stage != Stage.Opening) return;
                    if (!RhinoApp.RunScript("_-Open \"" + _files[_index] + "\"", false) || !IsCurrent(RhinoDoc.ActiveDoc))
                        throw new InvalidOperationException("打开文件失败或活动文档不符：" + _files[_index]);
                }
                // Rhino 命令可能泵送消息并触发关闭/停止事件，不得覆盖已暂停状态。
                if (_stage != Stage.Opening) return;
                _stage = Stage.Waiting;
                _checks = _stableCount = 0;
                _signature = null;
                _summary = "等待当前文件的 Guid、材质、颜色及前缀刷新稳定。";
                Refresh(true);
            });
        }

        /// <summary>冻结每次读到的输入，稳定检查和实际执行使用同一份参数。</summary>
        private sealed class Inputs
        {
            public readonly List<GH_Guid> Ids = new List<GH_Guid>();
            public readonly List<string> Materials = new List<string>();
            public readonly List<string> Colors = new List<string>();
            public readonly List<string> Prefixes = new List<string>();
            public string Database = "";
            public string Connector = "-";
            public int Digits = 4;
            public int Start = 1;
            public uint DocumentSerial;
        }

        private void CheckInputs(IGH_DataAccess da)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (!IsCurrent(doc)) throw new InvalidOperationException("活动文档已改变，请返回当前待处理文件后继续。");
            var input = new Inputs { DocumentSerial = doc.RuntimeSerialNumber };
            da.GetDataList(1, input.Ids);
            da.GetDataList(2, input.Materials);
            da.GetDataList(3, input.Colors);
            da.GetData(4, ref input.Database);
            da.GetDataList(5, input.Prefixes);
            da.GetData(6, ref input.Digits);
            da.GetData(7, ref input.Connector);
            da.GetData(8, ref input.Start);
            input.Database = NormalizePath(input.Database);
            if (!string.Equals(input.Database, _batchDatabase, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("批次运行期间数据库地址发生变化，请恢复原地址后继续，或重新运行整批。");
            _checks++;
            bool ready = input.Ids.Count > 0 && input.Ids.All(id => id != null && id.Value != Guid.Empty && doc.Objects.FindId(id.Value) != null);
            string signature = InputSignature(input, doc);
            _stableCount = ready && signature == _signature ? _stableCount + 1 : 0;
            _signature = signature;
            // 三次相同快照，且每次间隔 500ms；所有上游也显式失效，避免只重算自身。
            if (_stableCount < 2)
            {
                if (_checks >= 40) throw new InvalidOperationException("等待输入稳定超时：请检查上游是否随当前 Rhino 文件刷新，以及 Guid 是否属于当前文档。");
                Refresh(true);
                return;
            }
            ValidateCount(input.Materials, input.Ids.Count, "材质");
            ValidateCount(input.Colors, input.Ids.Count, "颜色");
            ValidateCount(input.Prefixes, input.Ids.Count, "编号前缀");
            if (input.Ids.Select(id => id.Value).Distinct().Count() != input.Ids.Count)
                throw new InvalidOperationException("Guid 存在重复，请先去重。");
            if (input.Digits < 1 || input.Digits > 99 || input.Start < 1)
                throw new InvalidOperationException("数字长度应为 1～99，起始序号应大于零。");
            _stage = Stage.Processing;
            _summary = $"正在编号并保存 {_index + 1}/{_files.Count}：{_files[_index]}";
            Defer(() => Process(input, signature));
        }

        private static void ValidateCount(List<string> values, int count, string name)
        {
            if (values.Count != 0 && values.Count != 1 && values.Count != count)
                throw new InvalidOperationException(name + "只能为空、单值或与 Guid 一一对应。");
        }

        // 手动电池按索引取文本，越界为空；批量电池明确支持单值广播，
        // 因此在调用共享入口前展开，保持手动电池原有行为不变。
        private static List<string> ExpandText(List<string> values, int count)
        {
            return values.Count == 1 ? Enumerable.Repeat(values[0], count).ToList() : values;
        }

        private string InputSignature(Inputs input, RhinoDoc doc)
        {
            // 长度前缀避免文本中的分隔符碰撞；几何校验值能识别同 Guid 对象的修改。
            IEnumerable<string> values = new[] { input.Database, input.Connector, input.Digits.ToString(),
                input.Start.ToString(), ClassificationSettingsText, doc.RuntimeSerialNumber.ToString(), input.Materials.Count.ToString() }
                .Concat(input.Materials).Concat(new[] { "颜色", input.Colors.Count.ToString() }).Concat(input.Colors)
                .Concat(new[] { "前缀", input.Prefixes.Count.ToString() }).Concat(input.Prefixes)
                .Concat(input.Ids.Select(id => id == null ? "空" : id.Value + ":" + doc.Objects.FindId(id.Value)?.Geometry?.DataCRC(0)));
            return string.Concat(values.Select(value => (value ?? "").Length + ":" + value));
        }

        private void Process(Inputs input, string signature)
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (!IsCurrent(doc) || doc.RuntimeSerialNumber != input.DocumentSerial || InputSignature(input, doc) != signature)
                throw new InvalidOperationException("准备执行期间模型或参数发生变化，请继续以重新获取输入。");
            var breps = new List<Brep>();
            var objects = input.Ids.Select(id => doc.Objects.FindId(id.Value)).ToList();
            try
            {
                foreach (RhinoObject obj in objects)
                {
                    GeometryBase geometry = obj.Geometry;
                    // Brep 输入复制；挤出等输入转换后的 Brep 已是独立对象，直接接管。
                    Brep brep = geometry is Brep source ? source.DuplicateBrep() : Brep.TryConvertBrep(geometry);
                    if (brep == null || !brep.IsValid || !brep.IsSolid)
                    {
                        brep?.Dispose();
                        throw new InvalidOperationException("对象不是有效闭合实体：" + obj.Id);
                    }
                    breps.Add(brep);
                }
                ClassifyEntities(ExpandText(input.Materials, objects.Count), ExpandText(input.Colors, objects.Count),
                    breps, input.Database, ExpandText(input.Prefixes, objects.Count), input.Digits, input.Connector, input.Start);
                if (_stage != Stage.Processing) return;
                StoreResults(input);
                if (ClassificationNumbers.Count != objects.Count || ClassificationNumbers.Any(string.IsNullOrWhiteSpace) ||
                    ClassificationSummary.StartsWith("错误：", StringComparison.Ordinal))
                    throw new InvalidOperationException("分类未全部成功，本文件未写回编号。" + ClassificationSummary);

                // 先入库确保编号已被占用，再修改模型；保存失败时停留本文件。
                // 重试会复用已入库编号，不能回滚数据库后让其它文件重用同一编号。
                PersistClassification();
                _fileStatuses[_index] = "已入库，正在写回编号及保存文件";
                var originals = objects.Select(obj => obj.Attributes.Duplicate()).ToList();
                uint undo = doc.BeginUndoRecord("批量自动编号_WB");
                try
                {
                    for (int i = 0; i < objects.Count; i++)
                    {
                        ObjectAttributes attributes = originals[i].Duplicate();
                        attributes.Name = ClassificationNumbers[i];
                        attributes.SetUserString("零件编号", ClassificationNumbers[i]);
                        if (!doc.Objects.ModifyAttributes(objects[i].Id, attributes, true))
                            throw new InvalidOperationException("写回对象属性失败：" + objects[i].Id);
                    }
                    SaveDocument(doc);
                }
                catch (Exception ex)
                {
                    // 模型端失败恢复原属性；数据库已提交的编号保留供继续时复用。
                    var restoreFailures = new List<string>();
                    for (int i = 0; i < objects.Count; i++)
                        if (!doc.Objects.ModifyAttributes(objects[i].Id, originals[i], true))
                            restoreFailures.Add(objects[i].Id.ToString());
                    throw new InvalidOperationException("数据库编号已保留。" + ex.Message +
                        (restoreFailures.Count == 0 ? " 模型原属性已恢复，可继续重试。" :
                        " 部分对象属性恢复失败，请检查：" + string.Join("、", restoreFailures)), ex);
                }
                finally { if (undo != 0) doc.EndUndoRecord(undo); }
                if (_stage != Stage.Processing) return;
                _fileStatuses[_index] = "成功：已入库、写入零件编号及对象名称，并保存原文件。" + ClassificationSummary;
                _completedDocumentSerial = doc.RuntimeSerialNumber;
                _stage = Stage.Publishing;
                _summary = $"文件 {_index + 1}/{_files.Count} 编号完成，正在通知下游保存编号。";
                _pulse = true;
                Refresh(false);
            }
            finally { foreach (Brep brep in breps) brep.Dispose(); }
        }

        private void StoreResults(Inputs input)
        {
            // 每次替换整个结果，不保留上一文件分支。固定路径也避免下游树匹配错位。
            _ids.Clear(); _numbers.Clear(); _matches.Clear();
            var path = new GH_Path(0);
            _ids.AppendRange(input.Ids, path);
            // 数据库读取等整体失败时也补足空结果，保持 Guid 与编号分支一一对应。
            _numbers.AppendRange(Enumerable.Range(0, input.Ids.Count).Select(i =>
                new GH_String(i < ClassificationNumbers.Count ? ClassificationNumbers[i] : "")), path);
            _matches.AppendRange(Enumerable.Range(0, input.Ids.Count).Select(i =>
                new GH_String(i < ClassificationStatuses.Count ? ClassificationStatuses[i] : ClassificationSummary)), path);
        }

        private void WaitForDownstream()
        {
            GH_Document document = OnPingDocument();
            if (document == null)
            {
                Pause("无法找到 GH 文档，不能等待下游解算。");
                return;
            }
            if (_completionDocument == document) return;
            DetachCompletionHandler();
            _completionDocument = document;
            document.SolutionEnd += OnDownstreamSolutionEnd;
        }

        /// <summary>
        /// 先让下游完整接收 True，再用独立的一轮解算发送 False。
        /// 两轮期间始终保持当前文档及结果，最后才保存下游改动并推进文件队列。
        /// </summary>
        private void OnDownstreamSolutionEnd(object sender, GH_SolutionEventArgs e)
        {
            if (_completionDocument == null || sender != _completionDocument) return;
            // 嵌套解算的结束不代表外层下游已完成，继续等待外层结束。
            if (_completionDocument.SolutionDepth > 1) return;
            DetachCompletionHandler();
            try
            {
                if (_stage == Stage.Publishing)
                {
                    _pulse = false;
                    _stage = Stage.ResettingPulse;
                    Refresh(false);
                }
                else if (_stage == Stage.ResettingPulse)
                {
                    _stage = Stage.Finishing;
                    Defer(FinishCurrentFile);
                }
            }
            catch (Exception ex) { Pause("下游解算结束处理失败：" + ex.Message); }
        }

        private void FinishCurrentFile()
        {
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (!IsCurrent(doc) || doc.RuntimeSerialNumber != _completedDocumentSerial)
                throw new InvalidOperationException("下游解算期间活动文档已变化，未继续切换文件。");
            // 最后一个文件也要保存下游写入，不能依赖“打开下一文件”时的保存动作。
            if (doc.Modified) SaveDocument(doc);
            _index++;
            if (_index == _files.Count)
            {
                _stage = Stage.Complete;
                Message = "完成";
                _summary = $"批量编号完成：{_files.Count} 个文件均已保存。";
                Refresh(false);
            }
            else OpenCurrent();
        }

        private void DetachCompletionHandler()
        {
            if (_completionDocument == null) return;
            _completionDocument.SolutionEnd -= OnDownstreamSolutionEnd;
            _completionDocument = null;
        }

        private static void SaveDocument(RhinoDoc doc)
        {
            if (string.IsNullOrWhiteSpace(doc.Path))
                throw new InvalidOperationException("当前文档尚未保存，请先为文档指定保存路径。");
            var options = new FileWriteOptions
            {
                WriteSelectedObjectsOnly = false,
                SuppressDialogBoxes = true,
                UpdateDocumentPath = true
            };
            if (!doc.WriteFile(doc.Path, options))
                throw new IOException("保存 Rhino 原文件失败：" + doc.Path);
        }

        /// <summary>文件操作保留到 GH 空闲再执行，消息循环重入时不丢弃任务。</summary>
        private void Defer(Action action)
        {
            int token = _generation;
            _timer?.Dispose();
            Timer timer = new Timer { Interval = 50 };
            _timer = timer;
            timer.Tick += (sender, e) =>
            {
                if (token != _generation || _timer != timer) return;
                if (_executingAction) return;
                GH_Document document = OnPingDocument();
                if (document != null && document.SolutionDepth > 0) return;
                if (!CanContinue(document)) return;
                timer.Stop(); timer.Dispose(); _timer = null;
                _executingAction = true;
                try { action(); }
                catch (Exception ex) { Pause(ex.Message); }
                finally { _executingAction = false; }
            };
            timer.Start();
        }

        /// <summary>
        /// 不依赖可能被其它解算取消的 ScheduleSolution。独立 UI 定时器持有刷新请求，
        /// 等外层解算及文件操作结束后主动解算；忙碌时保留请求到下一次 Tick。
        /// </summary>
        private void Refresh(bool upstream)
        {
            _refreshUpstream |= upstream;
            if (_refreshTimer != null) return;
            int token = _generation;
            Timer timer = new Timer { Interval = upstream ? 500 : 50 };
            _refreshTimer = timer;
            timer.Tick += (sender, e) =>
            {
                if (token != _generation || _refreshTimer != timer) return;
                if (_executingAction) return;
                GH_Document document = OnPingDocument();
                if (document != null && document.SolutionDepth > 0) return;
                if (!CanContinue(document)) return;
                timer.Stop(); timer.Dispose(); _refreshTimer = null;
                bool refreshSources = _refreshUpstream;
                _refreshUpstream = false;
                try
                {
                    if (refreshSources)
                    {
                        var visited = new HashSet<IGH_DocumentObject>();
                        foreach (IGH_Param source in Params.Input.SelectMany(param => param.Sources))
                            ExpireUpstream(source, visited);
                    }
                    // 当前已处于 UI 空闲，直接请求解算，不再经过易丢失的一次性调度。
                    int solveCount = _solveCount;
                    ExpireSolution(true);
                    // 输入类型错误等可能令 GH 跳过 SolveInstance，不能误以为请求已完成。
                    if (_solveCount == solveCount && _stage != Stage.Paused)
                        Pause("已请求解算，但电池未进入解算；请检查输入参数错误及文档状态。", false);
                }
                catch (Exception ex) { Pause("刷新输入或请求解算失败：" + ex.Message, false); }
            };
            timer.Start();
        }

        private bool CanContinue(GH_Document document)
        {
            string reason = document == null ? "GH 文档已失效。" :
                Locked ? "批量编号电池已被禁用。" :
                !GH_Document.EnableSolutions ? "Grasshopper 全局求解器已关闭。" :
                !document.Enabled ? "当前 GH 文档未启用。" : null;
            if (reason == null) return true;
            if (_stage != Stage.Paused) Pause(reason);
            else CancelPending();
            return false;
        }

        private void ExpireUpstream(IGH_Param parameter, HashSet<IGH_DocumentObject> visited)
        {
            IGH_DocumentObject owner = parameter.Attributes?.GetTopLevel.DocObject ?? parameter;
            if (owner == this || !visited.Add(owner)) return;
            if (owner is GH_Component component)
            {
                foreach (IGH_Param source in component.Params.Input.SelectMany(param => param.Sources))
                    ExpireUpstream(source, visited);
                component.ExpireSolution(false);
            }
            else
            {
                foreach (IGH_Param source in parameter.Sources) ExpireUpstream(source, visited);
                parameter.ExpireSolution(false);
            }
        }

        private void Pause(string reason, bool refresh = true)
        {
            string file = _index < _files.Count ? _files[_index] : "未开始处理文件";
            string stage = StageDescription(_stage);
            CancelPending();
            _stage = Stage.Paused;
            _summary = $"已暂停：{reason} 阶段：{stage}；文件：{file}";
            Message = "已暂停：" + stage;
            if (_index < _fileStatuses.Count) _fileStatuses[_index] = _summary;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, _summary);
            RhinoApp.WriteLine(_summary);
            OnDisplayExpired(true);
            GH_Document document = OnPingDocument();
            if (refresh && document != null && document.Enabled && !Locked && GH_Document.EnableSolutions)
                Refresh(false);
        }

        private static string StageDescription(Stage stage)
        {
            switch (stage)
            {
                case Stage.Opening: return "打开文件";
                case Stage.Waiting: return "等待输入稳定";
                case Stage.Processing: return "分类编号及保存";
                case Stage.Publishing: return "通知下游";
                case Stage.ResettingPulse: return "复位完成脉冲";
                case Stage.Finishing: return "保存下游改动并切换文件";
                case Stage.Paused: return "暂停";
                case Stage.Complete: return "批次完成";
                default: return "启动批次";
            }
        }

        private void CancelPending()
        {
            _generation++;
            _pulse = false;
            DetachCompletionHandler();
            _timer?.Stop(); _timer?.Dispose(); _timer = null;
            _refreshTimer?.Stop(); _refreshTimer?.Dispose(); _refreshTimer = null;
            _refreshUpstream = false;
        }

        internal void RequestBatchRun() { _requested = true; ExpireSolution(true); }
        internal void StopBatch()
        {
            if (_stage == Stage.Opening || _stage == Stage.Waiting || _stage == Stage.Processing ||
                _stage == Stage.Publishing || _stage == Stage.ResettingPulse || _stage == Stage.Finishing)
                Pause("用户停止，继续时从当前文件重新检查。");
        }
        internal void ContinueBatch()
        {
            if (_stage != Stage.Paused || _index >= _files.Count) return;
            OpenCurrent();
            Refresh(false);
        }

        public override void CreateAttributes() { m_attributes = new BatchAutoNumberWbAttributes(this); }
        public override void RemovedFromDocument(GH_Document document)
        {
            Pause("批量编号电池已从 GH 文档移除。", false);
            base.RemovedFromDocument(document);
        }
        public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
        {
            if (context == GH_DocumentContext.Close)
            {
                Pause("GH 文档已关闭，批次停止。", false);
            }
            // 批量组件不调用手动分类的关闭提示；编号入库由文件处理步骤负责。
        }
    }

    internal sealed class BatchAutoNumberWbAttributes : GH_ComponentAttributes
    {
        public BatchAutoNumberWbAttributes(BatchAutoNumberWb owner) : base(owner) { }
        protected override void Layout()
        {
            base.Layout();
            Bounds = new RectangleF(Bounds.X, Bounds.Y, Bounds.Width, Bounds.Height + 28);
        }
        private RectangleF Button(int index)
        {
            float width = (Bounds.Width - 14) / 3;
            return new RectangleF(Bounds.X + 5 + index * (width + 2), Bounds.Bottom - 24, width, 20);
        }
        protected override void Render(GH_Canvas canvas, Graphics graphics, GH_CanvasChannel channel)
        {
            base.Render(canvas, graphics, channel);
            if (channel != GH_CanvasChannel.Objects) return;
            string[] labels = { "运行", "停止", "继续" };
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                for (int i = 0; i < labels.Length; i++)
                {
                    using (GH_Capsule capsule = GH_Capsule.CreateCapsule(Button(i), GH_Palette.Black))
                        capsule.Render(graphics, Selected, Owner.Locked, Owner.Hidden);
                    graphics.DrawString(labels[i], GH_FontServer.Small, Brushes.White, Button(i), format);
                }
        }
        public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
        {
            if (!Owner.Locked && !Owner.Hidden && e.Button == MouseButtons.Left)
            {
                var owner = (BatchAutoNumberWb)Owner;
                for (int i = 0; i < 3; i++)
                    if (Button(i).Contains(e.CanvasLocation))
                    {
                        if (i == 0) owner.RequestBatchRun();
                        else if (i == 1) owner.StopBatch();
                        else owner.ContinueBatch();
                        return GH_ObjectResponse.Handled;
                    }
            }
            return base.RespondToMouseDown(sender, e);
        }
    }
}
