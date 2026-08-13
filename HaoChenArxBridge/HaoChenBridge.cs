using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Gssoft.Gscad.ApplicationServices;
using Gssoft.Gscad.DatabaseServices;
using Gssoft.Gscad.EditorInput;
using Gssoft.Gscad.Geometry;
using Gssoft.Gscad.Runtime;

[assembly: CommandClass(typeof(HaoChenArxBridge.HaoChenBridge))]
[assembly: ExtensionApplication(typeof(HaoChenArxBridge.HaoChenBridge))]
namespace HaoChenArxBridge;

public sealed class HaoChenBridge : IExtensionApplication
{
    internal const string PipeName = "Parrot.HaoChen2GH.v1";
    private static readonly string LogFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Parrot", "HaoChen2GH.log");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static CancellationTokenSource? serverCancellation;
    private static TaskCompletionSource<HaoChenImportResponse>? pendingSelection;
    private static List<string>? pendingHandlesToSelect;
    private static int selectionRequested;

    public void Initialize()
    {
        Application.Idle += OnApplicationIdle;
        Log("Initialize; subscribed to application idle event.");
        StartServer();
        WriteMessage("\nHaoChen2GH 已就绪。可在 Grasshopper 直接点击导入。");
    }

    public void Terminate()
    {
        Application.Idle -= OnApplicationIdle;
        serverCancellation?.Cancel();
        serverCancellation = null;
    }

    [CommandMethod("HAOCHEN2GHSTART")]
    public void Start()
    {
        if (!StartServer()) { WriteMessage("\nHaoChen2GH 服务已经启动。"); return; }
        WriteMessage("\nHaoChen2GH 服务已启动。请回到 Grasshopper 点击“从浩辰导入”。");
    }

    [CommandMethod("HAOCHEN2GHSTOP")]
    public void Stop() { serverCancellation?.Cancel(); serverCancellation = null; WriteMessage("\nHaoChen2GH 服务已停止。"); }

    [CommandMethod("HAOCHEN2GHPICK")]
    public void PickFromGrasshopper()
    {
        TaskCompletionSource<HaoChenImportResponse>? completion = Interlocked.Exchange(ref pendingSelection, null);
        if (completion == null)
            return;

        try { completion.TrySetResult(ReadSelectedBlocksOnCommandThread()); }
        catch (System.Exception ex) { completion.TrySetResult(HaoChenImportResponse.Fail(ex.Message)); }
    }

    [CommandMethod("HAOCHEN2GHSELECT")]
    public void SelectFromGrasshopper()
    {
        if (Volatile.Read(ref pendingSelection) == null || Volatile.Read(ref pendingHandlesToSelect) == null) return;
        try
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) { CompleteSelection(HaoChenImportResponse.Fail("没有打开的浩辰图纸。")); return; }
            document.SendStringToExecute("_.ZOOM _E HAOCHEN2GHMARKSELECT ", true, false, false);
        }
        catch (System.Exception ex) { CompleteSelection(HaoChenImportResponse.Fail("缩放失败: " + ex.Message)); }
    }

    [CommandMethod("HAOCHEN2GHMARKSELECT")]
    public void MarkSelectedObjects()
    {
        List<string>? handles = Interlocked.Exchange(ref pendingHandlesToSelect, null);
        if (handles == null) return;
        try
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null) { CompleteSelection(HaoChenImportResponse.Fail("没有打开的浩辰图纸。")); return; }
            var ids = new ObjectIdCollection();
            foreach (string handle in handles)
            {
                long value;
                if (!long.TryParse(handle, System.Globalization.NumberStyles.HexNumber, null, out value)) continue;
                try { ids.Add(document.Database.GetObjectId(false, new Handle(value), 0)); } catch { }
            }
            document.Editor.SetImpliedSelection(ids.Cast<ObjectId>().ToArray());
            CompleteSelection(new HaoChenImportResponse());
        }
        catch (System.Exception ex) { CompleteSelection(HaoChenImportResponse.Fail("选中并放大失败: " + ex.Message)); }
    }

    private static void CompleteSelection(HaoChenImportResponse response) => Interlocked.Exchange(ref pendingSelection, null)?.TrySetResult(response);

    private static bool StartServer()
    {
        if (serverCancellation != null)
            return false;

        serverCancellation = new CancellationTokenSource();
        _ = RunServerAsync(serverCancellation.Token);
        Log("Named pipe service started.");
        return true;
    }

    private static async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Log("Creating pipe server.");
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                Log("Pipe client connected.");
                using var reader = new StreamReader(pipe, leaveOpen: true);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                string? request = await reader.ReadLineAsync(cancellationToken);
                Log("Pipe request=" + (request ?? "<null>"));
                HaoChenImportResponse response;
                try
                {
                    response = request == "PICK" ? await ReadSelectedBlocksAsync() : request != null && request.StartsWith("SELECT:", StringComparison.Ordinal) ? await SelectByHandlesAsync(request.Substring(7)) : request == "PING" ? new HaoChenImportResponse() : HaoChenImportResponse.Fail("不支持的 HaoChen2GH 请求。");
                }
                catch (System.Exception ex)
                {
                    response = HaoChenImportResponse.Fail("浩辰请求处理失败: " + ex.Message);
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonOptions));
                Log("Pipe response sent; error=" + response.Error + "; regions=" + response.Regions.Count);
                string? acknowledgement = await reader.ReadLineAsync(cancellationToken);
                Log("Pipe acknowledgement=" + (acknowledgement ?? "<null>"));
            }
            catch (OperationCanceledException) { return; }
            catch (System.Exception ex) { Log("Pipe server exception: " + ex); }
        }
    }

    private static Task<HaoChenImportResponse> ReadSelectedBlocksAsync()
    {
        var completion = new TaskCompletionSource<HaoChenImportResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref pendingSelection, completion, null) != null)
            return Task.FromResult(HaoChenImportResponse.Fail("已有一个来自 Grasshopper 的导入请求正在等待选择。"));

        Interlocked.Exchange(ref selectionRequested, 1);
        Log("Selection request registered; waiting for application idle event.");
        return completion.Task;
    }

    private static Task<HaoChenImportResponse> SelectByHandlesAsync(string handleText)
    {
        var completion = new TaskCompletionSource<HaoChenImportResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Interlocked.CompareExchange(ref pendingSelection, completion, null) != null) return Task.FromResult(HaoChenImportResponse.Fail("浩辰正忙于另一项请求。"));
        pendingHandlesToSelect = handleText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        Interlocked.Exchange(ref selectionRequested, 2);
        return completion.Task;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        int requestKind = Volatile.Read(ref selectionRequested);
        if (requestKind == 0)
            return;

        var completion = Volatile.Read(ref pendingSelection);
        if (completion == null)
        {
            Interlocked.Exchange(ref selectionRequested, 0);
            return;
        }

        try
        {
            var document = Application.DocumentManager.MdiActiveDocument;
            if (document == null)
            {
                Log("Idle event: no active document; request retained.");
                return;
            }

            if (Interlocked.CompareExchange(ref selectionRequested, 0, requestKind) != requestKind)
                return;

            document.SendStringToExecute(requestKind == 1 ? "HAOCHEN2GHPICK " : "HAOCHEN2GHSELECT ", true, false, false);
            Log("Idle event queued HaoChen2GH request=" + requestKind);
        }
        catch (System.Exception ex)
        {
            Log("Idle event exception: " + ex);
            if (Interlocked.CompareExchange(ref pendingSelection, null, completion) == completion)
            {
                completion.TrySetResult(HaoChenImportResponse.Fail("无法启动浩辰选择命令: " + ex.Message));
            }
        }
    }

    private static HaoChenImportResponse ReadSelectedBlocksOnCommandThread()
    {
        var document = Application.DocumentManager.MdiActiveDocument;
        if (document == null)
            return HaoChenImportResponse.Fail("没有打开的浩辰图纸。");

        var selection = document.Editor.GetSelection(new PromptSelectionOptions { MessageForAdding = "\n选择要导入到 Grasshopper 的块：" });
        if (selection.Status != PromptStatus.OK)
            return HaoChenImportResponse.Fail("选择已取消。");

        var watch = Stopwatch.StartNew();
        var regions = new List<HaoChenRegionDto>();
        var errors = new List<string>();
        var diagnostics = new List<string> { "选择对象数=" + selection.Value.Count };
        using var transaction = document.Database.TransactionManager.StartTransaction();
        foreach (SelectedObject selected in selection.Value)
        {
            if (selected?.ObjectId.IsNull != false)
            {
                diagnostics.Add("选择项：无效 ObjectId");
                continue;
            }
            DBObject item = transaction.GetObject(selected.ObjectId, OpenMode.ForRead);
            diagnostics.Add("选择项类型=" + item.GetType().Name + "，句柄=" + item.Handle);
            if (item is BlockReference block)
                ReadBlockRegion(block, transaction, regions, errors, diagnostics, document.Name);
            else if (item is Curve curve)
                ReadCurveEntity(curve, item, regions, document.Name);
            else if (item is DBPoint point)
                ReadPointEntity(point, regions, document.Name);
            else if (item is DBText text)
                ReadTextEntity(text.Position, text.TextString, text.Height, text.Rotation, text, regions, document.Name);
            else if (item is MText mText)
                ReadTextEntity(mText.Location, mText.Contents, mText.TextHeight, mText.Rotation, mText, regions, document.Name);
            else
                errors.Add("Handle=" + item.Handle + " | 暂不支持的实体类型：" + item.GetType().Name + "。");
        }
        transaction.Commit();
        watch.Stop();
        return new HaoChenImportResponse
        {
            Regions = regions,
            ElapsedMilliseconds = watch.ElapsedMilliseconds,
            Error = string.Join(Environment.NewLine, errors),
            Diagnostics = diagnostics
        };
    }

    private static void ReadCurveEntity(Curve curve, DBObject item, List<HaoChenRegionDto> output, string fileName)
    {
        var edge = new HaoChenLoopDto();
        foreach (Point3d point in SampleCurve(curve, 64)) edge.Points.Add(new HaoChenPointDto(point.X, point.Y, point.Z));
        if (edge.Points.Count < 2) return;
        output.Add(new HaoChenRegionDto { Kind = "Curve", Handle = item.Handle.ToString(), Layer = ((Entity)item).Layer, Color = ((Entity)item).Color.ColorValue.ToArgb(), LineType = ((Entity)item).Linetype, FileName = fileName, Loops = new List<HaoChenLoopDto> { edge } });
    }

    private static void ReadPointEntity(DBPoint point, List<HaoChenRegionDto> output, string fileName)
    {
        output.Add(new HaoChenRegionDto { Kind = "Point", Handle = point.Handle.ToString(), Layer = point.Layer, Color = point.Color.ColorValue.ToArgb(), LineType = point.Linetype, FileName = fileName, Position = new HaoChenPointDto(point.Position.X, point.Position.Y, point.Position.Z) });
    }

    private static void ReadTextEntity(Point3d position, string text, double height, double rotation, Entity entity, List<HaoChenRegionDto> output, string fileName)
    {
        output.Add(new HaoChenRegionDto { Kind = "Text", Handle = entity.Handle.ToString(), Layer = entity.Layer, Color = entity.Color.ColorValue.ToArgb(), LineType = entity.Linetype, FileName = fileName, Text = text ?? string.Empty, TextHeight = height, Rotation = rotation, Position = new HaoChenPointDto(position.X, position.Y, position.Z) });
    }

    private static void ReadBlockRegion(BlockReference block, Transaction transaction, List<HaoChenRegionDto> output, List<string> errors, List<string> diagnostics, string fileName)
    {
        var definition = (BlockTableRecord)transaction.GetObject(block.BlockTableRecord, OpenMode.ForRead);
        string blockTag = "BlockName=" + definition.Name + " | Handle=" + block.Handle;
        var exploded = new DBObjectCollection();
        block.Explode(exploded);
        diagnostics.Add(blockTag + " | 展开实体数=" + exploded.Count + " | 类型=" + string.Join(", ", exploded.Cast<DBObject>().Select(item => item.GetType().Name)));

        try
        {
            var regions = exploded.OfType<Region>().ToList();
            diagnostics.Add(blockTag + " | Region 数量=" + regions.Count);
            if (regions.Count == 0)
            {
                errors.Add(blockTag + " | 未找到有效的 Region。");
                return;
            }
            if (regions.Count > 1)
            {
                errors.Add(blockTag + " | 展开后包含多个 Region，仅支持单一 Region 块。");
                return;
            }

            Region region = regions[0];
            var curves = new DBObjectCollection();
            region.Explode(curves);
            diagnostics.Add(blockTag + " | Region 边界实体数=" + curves.Count + " | 类型=" + string.Join(", ", curves.Cast<DBObject>().Select(item => item.GetType().Name)));
            var edges = new List<HaoChenLoopDto>();
            foreach (DBObject item in curves)
            {
                if (item is not Curve curve) continue;
                using (curve)
                {
                    var edge = new HaoChenLoopDto();
                    int pointCount = 0;
                    foreach (Point3d point in SampleCurve(curve, 32))
                    {
                        edge.Points.Add(new HaoChenPointDto(point.X, point.Y, point.Z));
                        pointCount++;
                    }
                    if (edge.Points.Count >= 2) edges.Add(edge);
                    diagnostics.Add(blockTag + " | 边界=" + curve.GetType().Name + " | 采样点=" + pointCount);
                }
            }
            diagnostics.Add(blockTag + " | 有效边界数=" + edges.Count);
            if (edges.Count == 0)
            {
                errors.Add(blockTag + " | Region 边界无法转换为点列。");
                return;
            }

            output.Add(new HaoChenRegionDto
            {
                Handle = block.Handle.ToString(),
                Kind = "Brep",
                BlockName = definition.Name,
                Layer = block.Layer,
                Color = block.Color.ColorValue.ToArgb(),
                LineType = block.Linetype,
                FileName = fileName,
                Loops = edges
            });
        }
        finally
        {
            foreach (DBObject item in exploded)
                item.Dispose();
        }
    }
    private static IEnumerable<Point3d> SampleCurve(Curve curve, int count)
    {
        double start = curve.StartParam;
        double end = curve.EndParam;
        for (int i = 0; i <= count; i++)
            yield return curve.GetPointAtParameter(start + (end - start) * i / count);
    }
    private static void WriteMessage(string message) => Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(message);
    private static void Log(string message)
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(LogFilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine);
        }
        catch { }
    }
}
public sealed class HaoChenImportResponse { public List<HaoChenRegionDto> Regions { get; set; } = new(); public long ElapsedMilliseconds { get; set; } public string Error { get; set; } = string.Empty; public List<string> Diagnostics { get; set; } = new(); public static HaoChenImportResponse Fail(string error) => new() { Error = error }; }
public sealed class HaoChenRegionDto { public string Kind { get; set; } = "Brep"; public string Handle { get; set; } = ""; public string BlockName { get; set; } = ""; public string Layer { get; set; } = ""; public int Color { get; set; } public string LineType { get; set; } = ""; public string FileName { get; set; } = ""; public HaoChenPointDto Position { get; set; } public string Text { get; set; } = ""; public double TextHeight { get; set; } public double Rotation { get; set; } public List<HaoChenLoopDto> Loops { get; set; } = new(); }
public sealed class HaoChenLoopDto { public List<HaoChenPointDto> Points { get; set; } = new(); }
public readonly record struct HaoChenPointDto(double X, double Y, double Z);
