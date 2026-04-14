Option Explicit On
Imports AutoCAD
Imports Autodesk.AutoCAD.DatabaseServices
Imports Grasshopper.Documentation
Imports Rhino.DocObjects
'Imports AXDBLib
Imports Rhino.Geometry
Imports Rg = Rhino.Geometry
Imports rd = Rhino.NodeInCode
' 定义一个结构体来替代元组
' 使用已提供的 RhinoResult 结构体
Public Structure RhinoResult
    Public Geometry As Object ' 几何对象
    Public Layer As String ' 图层
    Public Color As Drawing.Color ' 颜色
    Public LineType As String ' 线型
    Public Handle As String ' 句柄
    Public BlockName As String ' 块名
    Public ErrorMessage As String ' 异常信息（如果有）

    ' 构造函数
    Public Sub New(geometry As Object, layer As String, color As Drawing.Color, lineType As String, handle As String, blockName As String, errorMessage As String)
        Me.Geometry = geometry
        Me.Layer = layer
        Me.Color = color
        Me.LineType = lineType
        Me.Handle = handle
        Me.BlockName = blockName
        Me.ErrorMessage = errorMessage
    End Sub
End Structure

Public Class AutoCADTool
    Private Declare Function FindWindow Lib "user32.dll" Alias "FindWindowA" (ByVal lpClassName As String, ByVal lpWindowName As String) As IntPtr
    Private Declare Function SetForegroundWindow Lib "user32.dll" (ByVal hwnd As IntPtr) As IntPtr
    ''' <summary>
    ''' CAD程序的实例
    ''' </summary>
    ''' <remarks></remarks>
    Public Shared TAcadApp As AcadApplication
    ''' <summary>
    ''' CAD文档的指针
    ''' </summary>
    ''' <remarks></remarks>
    Private Shared iAcadDoc As AcadDocument


    Public Shared ReadOnly Property AcadApp As AcadApplication
        Get
            If TAcadApp Is Nothing Then
                ConnectCAD()
            End If
            Return TAcadApp
        End Get
    End Property







    ''' <summary>
    ''' 新建一个cad文档
    ''' </summary>
    Public Shared Sub Add_CAD_NewDocument()
        TAcadApp.Documents.Add()
    End Sub





    ''' <summary>
    ''' 在屏幕上选择元素，元素类型由TypeOfSelectObject指定
    ''' entityType是CAD内部定义的组码
    ''' </summary>
    ''' <param name="filterCode"></param>过滤器类型的组码
    ''' <param name="filterStr"></param>实体类型的组码
    ''' <returns></returns>
    Public Shared Function GetSelectionSetOnScreen(filterCode As Integer, filterStr As String) As AcadSelectionSet
        Dim ssetObj As AcadSelectionSet
        On Error Resume Next
        iAcadDoc.SelectionSets().Item("sset_object").Delete()
        ssetObj = iAcadDoc.SelectionSets().Add("sset_object")
        On Error GoTo 0
        If Not (ssetObj Is Nothing) Then
            MySetForegroundWindow(TAcadApp.Caption()) '将CAD窗口前置
            Dim FilterType(0 To 0) As Int16  '注意VBA中的integer类型在VB.Net中对应的是int16
            Dim FilterData(0 To 0) As Object
            FilterType(0) = filterCode
            FilterData(0) = filterStr
            ssetObj.SelectOnScreen(FilterType, FilterData)
            If ssetObj.Count = 0 Then
                ssetObj = Nothing
            End If
        End If
        Return ssetObj
    End Function


    ''' <summary>
    ''' 从屏幕获取选择集，对象类型为直线和文本
    ''' </summary>
    ''' <returns></returns>
    Public Shared Function GetSelectionSetOnScreen() As AcadSelectionSet
        On Error Resume Next
        iAcadDoc = TAcadApp.ActiveDocument
        If iAcadDoc Is Nothing Then
            MsgBox("没有活动的CAD文档！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
            Return Nothing
        End If
        On Error GoTo 0

        Dim ssetObj As AcadSelectionSet
        On Error Resume Next
        IsModelSpace = CheckModelSpaceIsActivety() '检查当前在模型空间还是图纸空间
        iAcadDoc.SelectionSets().Item("sset_object").Delete()
        ssetObj = iAcadDoc.SelectionSets().Add("sset_object")
        If ssetObj IsNot Nothing Then
            MySetForegroundWindow(TAcadApp.Caption()) '将CAD窗口前置
            'Dim FilterType(0 To 3) As Int16  '注意VBA中的integer类型在VB.Net中对应的是int16
            'Dim FilterData(0 To 3) As Object
            ''设置过滤器类型 
            'FilterType(0) = -4
            'FilterType(1) = 0
            'FilterType(2) = 0
            'FilterType(3) = 0
            'FilterType(4) = 0
            'FilterType(4) = -4
            ''设置过滤数据
            'FilterData(0) = "<or"
            'FilterData(1) = "LINE"
            'FilterData(2) = "TEXT"
            'FilterData(3) = "CIRCLE"
            'FilterData(4) = "ARC"
            'FilterData(4) = "PLINE"
            'FilterData(4) = "ELLIPSE"
            'FilterData(4) = "REGION"
            'FilterData(3) = "or>"

            'ssetObj.SelectOnScreen(FilterType, FilterData)
            ssetObj.SelectOnScreen()
            If (ssetObj.Count = 0) Then
                Return Nothing
            End If
        End If
        Return ssetObj
    End Function




    'Public Shared Function ConnectCAD2(Optional visible As Boolean = True) As Boolean
    '    On Error Resume Next
    '    TAcadApp = GetObject(, "AutoCAD.Application.23")
    '    If TAcadApp Is Nothing Then
    '        On Error Resume Next
    '        TAcadApp = CreateObject("AutoCAD.Application.23", )
    '        If TAcadApp Is Nothing Then
    '            MsgBox("无法连接或打开AutoCAD，请检查AutoCAD是否安装正确！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
    '            Return False
    '        End If
    '    End If
    '    TAcadApp.Visible = visible
    '    Return True
    'End Function


    '''<summary>
    ''' 连接CAD
    ''' </summary>
    ''' <returns>连接成功返回true，否则返回false</returns>
    ''' <remarks></remarks>
    Public Shared Function ConnectCAD(Optional visible As Boolean = True) As Boolean
        ' 使用 Object() 数组代替 Variant + Array()
        Dim acadVersions As Object() = New Object() {
        New Object() {2020, "AutoCAD.Application.23.1"},
        New Object() {2025, "AutoCAD.Application.25.0"},
        New Object() {2026, "AutoCAD.Application.25.1"},
        New Object() {2024, "AutoCAD.Application.24.3"},
        New Object() {2023, "AutoCAD.Application.24.2"},
        New Object() {2022, "AutoCAD.Application.24.1"},
        New Object() {2021, "AutoCAD.Application.24"},
        New Object() {2019, "AutoCAD.Application.23"},
        New Object() {2018, "AutoCAD.Application.22"}}

        Dim cad2020 As String = "AutoCAD.Application.23"
        ' 遍历版本
        For Each versionPair As Object In acadVersions
            Dim pair As Object() = CType(versionPair, Object())
            Dim progID As String = pair(1).ToString()

            Try
                ' 尝试获取已打开实例
                TAcadApp = GetObject(, cad2020)
            Catch ex As Exception
                TAcadApp = Nothing
            End Try

            If TAcadApp Is Nothing Then
                Try
                    ' 创建实例
                    TAcadApp = CreateObject(progID)
                Catch ex As Exception
                    TAcadApp = Nothing
                End Try
            End If

            If TAcadApp IsNot Nothing Then
                TAcadApp.Visible = visible
                Return True
            End If
        Next

        ' 所有版本尝试失败
        MsgBox("无法连接或打开AutoCAD，请检查AutoCAD是否安装正确！", MsgBoxStyle.MsgBoxSetForeground Or MsgBoxStyle.OkOnly Or MsgBoxStyle.SystemModal Or MsgBoxStyle.Exclamation, "错误信息")
        Return False
    End Function

    Private Shared Function CheckCadOK() As Boolean
        If TAcadApp Is Nothing Then
            If ConnectCAD() = False Then
                Return False
            End If
        End If
        iAcadDoc = TAcadApp.ActiveDocument
        If iAcadDoc Is Nothing Then
            MsgBox("没有活动的CAD文档！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
            Return False
        End If
        Return True
    End Function

    ''' <summary>
    ''' 判断是当前是在模型空间还是在图纸空间
    ''' </summary>
    ''' <returns></returns>true表示在模型空间，false表示在图纸空间
    Private Shared Function CheckModelSpaceIsActivety() As Boolean
        If iAcadDoc.GetVariable("CTAB") = "Model" Then
            CheckModelSpaceIsActivety = True
        Else
            CheckModelSpaceIsActivety = False
        End If
    End Function

    Private Shared IsModelSpace As Boolean


    Public Shared Sub MySetForegroundWindow(ByVal caption As String)
        Dim hwnd As Long = FindWindow(vbNullString, caption)
        SetForegroundWindow(hwnd) '将hwnd 代表的窗口前置
    End Sub



    ''' <summary>
    ''' 求解P1和P2的距离
    ''' </summary>
    ''' <param name="p1"></param>点1
    ''' <param name="p2"></param>点2
    ''' <returns></returns>
    Function GetDistanceFor2Points(p1() As Double, p2() As Double) As Double
        GetDistanceFor2Points = Math.Sqrt((p1(0) - p2(0)) ^ 2 + (p1(1) - p2(1)) ^ 2 + (p1(2) - p2(2)) ^ 2)
    End Function

    ''' <summary>
    ''' 几个数中最小的
    ''' </summary>
    ''' <param name="a"></param>
    ''' <param name="b"></param>
    ''' <param name="c"></param>
    ''' <param name="d"></param>
    ''' <returns></returns>
    Function Min(a As Double, b As Double, c As Double, d As Double) As Double
        If a <= b And a <= c And a <= d Then
            Min = a
        ElseIf b <= a And b <= c And b <= d Then
            Min = b
        ElseIf c <= a And c <= b And c <= d Then
            Min = c
        Else
            Min = d
        End If
    End Function




    ' 这个函数负责遍历 CAD 选择集并返回所有 Rhino 对象
    Public Shared Function CAD2Rhino() As (List(Of Object), List(Of String), List(Of Drawing.Color), List(Of String), List(Of String), List(Of String))
        Dim sset As AcadSelectionSet = GetSelectionSetOnScreen()
        Dim theGeo As List(Of Object) = New List(Of Object)()
        Dim theLayer As List(Of String) = New List(Of String)()
        Dim theColor As List(Of Drawing.Color) = New List(Of Drawing.Color)()
        Dim theLineType As List(Of String) = New List(Of String)()
        Dim theHandle As List(Of String) = New List(Of String)()
        Dim theBlockName As List(Of String) = New List(Of String)()

        If sset IsNot Nothing Then
            For Each item In sset
                Dim value = ConvertCADItemToRhino(item)
                If value.Item1 IsNot Nothing Then
                    theGeo.Add(value.Item1)
                    theLayer.Add(value.Item2)
                    theColor.Add(value.Item3)
                    theLineType.Add(value.Item4)
                    theHandle.Add(value.Item5)
                    theBlockName.Add(value.Item6)
                End If
            Next
        End If

        Return (theGeo, theLayer, theColor, theLineType, theHandle, theBlockName)
    End Function



    Public Shared Function ConvertCADItemToRhino(item As Object) As (Object, String, Drawing.Color, String, String, String)
        If TypeOf (item) Is AutoCAD.AcadLine Then
            Return CAD_Line2Rhino_LineCurve(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadCircle Then
            Return CAD_Circle2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadArc Then
            Return CAD_Arc2Rhino_ArcCurve(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadPoint Then
            Return CAD_Point2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadLWPolyline Then
            Return CAD_Polyline2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadSpline Then
            Return CAD_Spline2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadEllipse Then
            Return CAD_Ellipse2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadRegion Then
            Return CAD_Region2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadText Or TypeOf (item) Is AutoCAD.AcadMText Then
            'Dim value = CAD_Text2Rhino(item)
            'Return (value, value.Item2, value.Item3, value.Item4)
            Return CAD_Text2Rhino(item)
        ElseIf TypeOf (item) Is AutoCAD.AcadBlockReference Then
            Return CAD_Block2Rhino_RegionOnly(item)
        Else
            ' 如果不认识的类型，返回 Nothing
            Return (Nothing, "", Drawing.Color.Empty, "", "", "")
        End If
    End Function







    Private Shared Function GetMidPointOnArc(arc As AutoCAD.AcadArc) As Double()
        ' 获取圆心、起点、终点、法向量
        Dim center As Double() = CType(arc.Center, Double())
        Dim startPt As Double() = CType(arc.StartPoint, Double())
        Dim endPt As Double() = CType(arc.EndPoint, Double())
        Dim normal As Double() = CType(arc.Normal, Double())

        ' 计算 Start 向量（相对圆心）
        Dim startVec(2) As Double
        For i As Integer = 0 To 2
            startVec(i) = startPt(i) - center(i)
        Next

        ' 检查向量长度是否有效
        Dim vecLen As Double = Math.Sqrt(startVec(0) ^ 2 + startVec(1) ^ 2 + startVec(2) ^ 2)
        Dim normalLen As Double = Math.Sqrt(normal(0) ^ 2 + normal(1) ^ 2 + normal(2) ^ 2)
        If vecLen < 0.000000000001 Or normalLen < 0.000000000001 Then
            ' 异常情况，返回 StartPoint 作为兜底
            Return startPt
        End If

        ' 计算弧旋转角度（弧度）
        Dim startAngle As Double = arc.StartAngle
        Dim endAngle As Double = arc.EndAngle
        Dim deltaAngle As Double = endAngle - startAngle
        ' 保证旋转角度在 0~2π
        If deltaAngle < 0 Then deltaAngle += 2 * Math.PI

        ' 旋转一半角度
        Dim midAngle As Double = deltaAngle / 2

        ' Rodrigues旋转公式计算中点向量
        Dim midVec As Double() = RotateVectorAroundAxis(startVec, normal, midAngle)

        ' 中点坐标 = 圆心 + 中点向量
        Dim midPt(2) As Double
        For i As Integer = 0 To 2
            midPt(i) = center(i) + midVec(i)
        Next

        Return midPt
    End Function

    ' ---------------------------------------
    ' Rodrigues公式：向量绕任意轴旋转
    Private Shared Function RotateVectorAroundAxis(vec As Double(), axis As Double(), angle As Double) As Double()
        ' 归一化轴向量
        Dim axisLen As Double = Math.Sqrt(axis(0) ^ 2 + axis(1) ^ 2 + axis(2) ^ 2)
        Dim u As Double = axis(0) / axisLen
        Dim v As Double = axis(1) / axisLen
        Dim w As Double = axis(2) / axisLen

        Dim x As Double = vec(0)
        Dim y As Double = vec(1)
        Dim z As Double = vec(2)

        Dim cosA As Double = Math.Cos(angle)
        Dim sinA As Double = Math.Sin(angle)

        ' Rodrigues旋转公式
        Dim rotated(2) As Double
        rotated(0) = u * (u * x + v * y + w * z) * (1 - cosA) + x * cosA + (-w * y + v * z) * sinA
        rotated(1) = v * (u * x + v * y + w * z) * (1 - cosA) + y * cosA + (w * x - u * z) * sinA
        rotated(2) = w * (u * x + v * y + w * z) * (1 - cosA) + z * cosA + (-v * x + u * y) * sinA

        Return rotated
    End Function

    Private Shared Function CAD_Line2Rhino_LineCurve(item As AutoCAD.AcadLine) As （Rhino.Geometry.LineCurve, String, Drawing.Color, String, String, String)
        Dim sp = item.StartPoint
        Dim sp_rh = New Rhino.Geometry.Point3d(sp(0)， sp(1)， sp(2))
        Dim ep = item.EndPoint
        Dim ep_rh = New Rhino.Geometry.Point3d(ep(0)， ep(1)， ep(2))
        Dim curve As Rhino.Geometry.LineCurve = New Rhino.Geometry.LineCurve(sp_rh, ep_rh)
        Dim layer As String = item.Layer
        Dim color As Drawing.Color
        Dim R As Integer = item.TrueColor.Red
        Dim G As Integer = item.TrueColor.Green
        Dim B As Integer = item.TrueColor.Blue
        color = Drawing.Color.FromArgb(R, G, B)
        Return (curve, layer, color, item.Linetype, item.Handle, "")
    End Function


    'Private Shared Function CAD_Circle2Rhino2(item As AutoCAD.AcadCircle) As (Rhino.Geometry.Circle, String, Drawing.Color, String, String)
    '    Dim center = item.Center
    '    Dim center_rh = New Rhino.Geometry.Point3d(center(0)， center(1)， center(2))
    '    Dim radius As Double = item.Radius
    '    Dim normal = item.Normal
    '    Dim normal_rh As Rhino.Geometry.Vector3d = New Rhino.Geometry.Vector3d(normal(0), normal(1), normal(2))
    '    Dim pl As Rhino.Geometry.Plane = New Rhino.Geometry.Plane(center_rh, normal_rh)
    '    Dim curve As Rhino.Geometry.Circle = New Rhino.Geometry.Circle(pl, center_rh, radius)
    '    Dim layer As String = item.Layer
    '    Dim color As Drawing.Color
    '    Dim R As Integer = item.TrueColor.Red
    '    Dim G As Integer = item.TrueColor.Green
    '    Dim B As Integer = item.TrueColor.Blue
    '    color = Drawing.Color.FromArgb(R, G, B)
    '    Return (curve, layer, color, item.Linetype, item.Handle)
    'End Function
    Private Shared Function CAD_Circle2Rhino(item As AutoCAD.AcadCircle) As (Rhino.Geometry.GeometryBase, String, Drawing.Color, String, String, String)

        Dim center = item.Center
        Dim center_rh As New Rhino.Geometry.Point3d(center(0), center(1), center(2))

        Dim radius As Double = item.Radius

        Dim normal = item.Normal
        Dim normal_rh As New Rhino.Geometry.Vector3d(normal(0), normal(1), normal(2))

        Dim pl As New Rhino.Geometry.Plane(center_rh, normal_rh)

        ' ✅ Circle → ArcCurve（关键）
        Dim circle As New Rhino.Geometry.Circle(pl, radius)
        Dim curve As New Rhino.Geometry.ArcCurve(circle)

        Dim layer As String = item.Layer

        Dim R As Integer = item.TrueColor.Red
        Dim G As Integer = item.TrueColor.Green
        Dim B As Integer = item.TrueColor.Blue
        Dim color As Drawing.Color = Drawing.Color.FromArgb(R, G, B)

        Return (curve, layer, color, item.Linetype, item.Handle, "")
    End Function

    Private Shared Function CAD_Arc2Rhino_ArcCurve(item As AutoCAD.AcadArc) As (Rhino.Geometry.GeometryBase, String, Drawing.Color, String, String, String)
        Dim sp = item.StartPoint
        Dim sp_rh As Rhino.Geometry.Point3d = New Rhino.Geometry.Point3d(sp(0), sp(1), sp(2))
        Dim ep = item.EndPoint
        Dim ep_rh = New Rhino.Geometry.Point3d(ep(0)， ep(1)， ep(2))
        Dim mp = GetMidPointOnArc(item)
        Dim mp_rh = New Rhino.Geometry.Point3d(mp(0)， mp(1)， mp(2))
        Dim arc As Rhino.Geometry.Arc = New Rhino.Geometry.Arc(sp_rh, mp_rh, ep_rh)
        Dim curve As Rhino.Geometry.ArcCurve = New Rhino.Geometry.ArcCurve(arc)
        Dim layer As String = item.Layer
        Dim color As Drawing.Color
        Dim R As Integer = item.TrueColor.Red
        Dim G As Integer = item.TrueColor.Green
        Dim B As Integer = item.TrueColor.Blue
        color = Drawing.Color.FromArgb(R, G, B)
        Return (curve, layer, color, item.Linetype, item.Handle, "")
    End Function




    Private Shared Function CAD_Point2Rhino(item As AutoCAD.AcadPoint) As (Rhino.Geometry.GeometryBase, String, Drawing.Color, String, String, String)

        ' 坐标
        Dim coordinate = item.Coordinates
        Dim pt As New Rhino.Geometry.Point3d(coordinate(0), coordinate(1), coordinate(2))

        ' ✅ Point3d → Point（关键）
        Dim pointGeo As New Rhino.Geometry.Point(pt)

        ' 图层
        Dim layer As String = item.Layer

        ' 颜色
        Dim R As Integer = item.TrueColor.Red
        Dim G As Integer = item.TrueColor.Green
        Dim B As Integer = item.TrueColor.Blue
        Dim color As Drawing.Color = Drawing.Color.FromArgb(R, G, B)

        Return (pointGeo, layer, color, item.Linetype, item.Handle, "")
    End Function

    Private Shared Function CAD_Polyline2Rhino(item As AutoCAD.AcadLWPolyline) As (Rhino.Geometry.GeometryBase, String, Drawing.Color, String, String, String)
        Dim item2 As AutoCAD.AcadLWPolyline = item.Copy
        Dim seg = item2.Explode()
        Dim pl_curve As Rhino.Geometry.Curve()
        ReDim pl_curve(seg.Length)
        item2.Delete()

        Dim count As Integer = seg.Length
        Dim i As Integer = 0
        For Each m In seg
            If TypeOf (m) Is AutoCAD.AcadLine Then
                Dim curve As Rhino.Geometry.LineCurve = CAD_Line2Rhino_LineCurve(m).Item1
                pl_curve(i) = curve

                Dim new_m As AutoCAD.AcadLine = m
                new_m.Delete()
            End If

            If TypeOf (m) Is AutoCAD.AcadArc Then
                Dim curve As Rhino.Geometry.ArcCurve = CAD_Arc2Rhino_ArcCurve(m).Item1
                pl_curve(i) = curve
                Dim new_m As AutoCAD.AcadArc = m
                new_m.Delete()
            End If
            i = i + 1
        Next
        Dim layer As String = item.Layer
        Dim color As Drawing.Color
        Dim R As Integer = item.TrueColor.Red
        Dim G As Integer = item.TrueColor.Green
        Dim B As Integer = item.TrueColor.Blue
        color = Drawing.Color.FromArgb(R, G, B)
        Return (Rhino.Geometry.Curve.JoinCurves(pl_curve, 0.01, False)(0), layer, color, item.Linetype, item.Handle, "")

    End Function

    Private Shared Function Rh_TextJustification(item As AutoCAD.AcadText) As TextJustification
        Dim textJust As TextJustification
        Select Case item.Alignment
            Case AutoCAD.AcAlignment.acAlignmentLeft '0
                textJust = TextJustification.Left

            Case AutoCAD.AcAlignment.acAlignmentCenter '1
                textJust = TextJustification.Center

            Case AutoCAD.AcAlignment.acAlignmentRight '2
                textJust = TextJustification.Right

            Case AutoCAD.AcAlignment.acAlignmentAligned '3
                textJust = TextJustification.None

            Case AutoCAD.AcAlignment.acAlignmentMiddle '4
                textJust = TextJustification.Middle

            Case AutoCAD.AcAlignment.acAlignmentFit '5
                textJust = TextJustification.None

            Case AutoCAD.AcAlignment.acAlignmentTopLeft '6
                textJust = TextJustification.TopLeft

            Case AutoCAD.AcAlignment.acAlignmentTopCenter '7
                textJust = TextJustification.TopCenter

            Case AutoCAD.AcAlignment.acAlignmentTopRight '8
                textJust = TextJustification.TopRight

            Case AutoCAD.AcAlignment.acAlignmentMiddleLeft '9
                textJust = TextJustification.MiddleLeft

            Case AutoCAD.AcAlignment.acAlignmentMiddleCenter '10
                textJust = TextJustification.MiddleCenter

            Case AutoCAD.AcAlignment.acAlignmentMiddleRight '11
                textJust = TextJustification.MiddleRight

            Case AutoCAD.AcAlignment.acAlignmentBottomLeft '12
                textJust = TextJustification.BottomLeft

            Case AutoCAD.AcAlignment.acAlignmentBottomCenter '13
                textJust = TextJustification.BottomCenter

            Case AutoCAD.AcAlignment.acAlignmentBottomRight '14
                textJust = TextJustification.BottomRight

        End Select
        Return textJust
    End Function

    Private Shared Function Rh_MTextJustification(item As AutoCAD.AcadMText) As TextJustification
        Dim textJust As TextJustification
        Select Case item.AttachmentPoint
            Case AcAttachmentPoint.acAttachmentPointTopLeft '1
                textJust = Rg.TextJustification.TopLeft

            Case AcAttachmentPoint.acAttachmentPointTopCenter '2
                textJust = Rg.TextJustification.TopCenter

            Case AcAttachmentPoint.acAttachmentPointTopRight '3
                textJust = Rg.TextJustification.TopRight

            Case AcAttachmentPoint.acAttachmentPointMiddleLeft '4
                textJust = Rg.TextJustification.MiddleLeft

            Case AcAttachmentPoint.acAttachmentPointMiddleCenter '5
                textJust = Rg.TextJustification.MiddleCenter

            Case AcAttachmentPoint.acAttachmentPointMiddleRight '6
                textJust = Rg.TextJustification.MiddleRight

            Case AcAttachmentPoint.acAttachmentPointBottomLeft '7
                textJust = Rg.TextJustification.BottomLeft

            Case AcAttachmentPoint.acAttachmentPointBottomCenter '8
                textJust = Rg.TextJustification.BottomCenter

            Case AcAttachmentPoint.acAttachmentPointBottomRight '9
                textJust = Rg.TextJustification.BottomRight

        End Select
        Return textJust
    End Function

    Private Shared Function RhinoTextJustification(item As Object) As TextJustification
        If TypeOf item Is AutoCAD.AcadText Then
            Return Rh_TextJustification(DirectCast(item, AutoCAD.AcadText))
        ElseIf TypeOf item Is AutoCAD.AcadMText Then
            Return Rh_MTextJustification(DirectCast(item, AutoCAD.AcadMText))
        Else
            Return Rg.TextJustification.Left
        End If
    End Function


    ''' <summary>
    ''' 将 AutoCAD 文本导入 Rhino
    ''' </summary>
    ''' <param name="item">AutoCAD 文本对象（AcadText 或 AcadMText）</param>
    ''' <returns>Tuple: (Rhino TextEntity, 图层名称, 颜色)</returns>
    Private Shared Function CAD_Text2Rhino3(item As Object) As (TextEntity, String, Drawing.Color, String)

        '========================
        ' 基础属性（安全获取）
        '========================
        Dim layer As String = ""
        Dim color As Drawing.Color = Drawing.Color.White
        Dim handle As String = ""

        Try : layer = item.Layer : Catch : End Try
        Try : handle = item.Handle : Catch : End Try

        Try
            color = Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)
        Catch
            color = Drawing.Color.White
        End Try

        '========================
        ' 通用变量
        '========================
        Dim textContent As String = ""
        Dim insertionPoint As Point3d = Point3d.Origin
        Dim height As Double = 1.0
        Dim rotation As Double = 0
        Dim hJust As Integer = 0
        Dim vJust As Integer = 0

        '========================
        ' TEXT
        '========================
        If TypeOf item Is AcadText Then

            Dim t As Object = item

            Try : textContent = t.TextString : Catch : End Try
            Try : height = t.Height : Catch : End Try
            Try : rotation = t.Rotation : Catch : End Try

            Try
                Dim pt = t.InsertionPoint
                insertionPoint = New Point3d(pt(0), pt(1), pt(2))
            Catch
            End Try

            Try : hJust = t.HorizontalJustification : Catch : hJust = 0 : End Try
            Try : vJust = t.VerticalJustification : Catch : vJust = 0 : End Try

            '========================
            ' MTEXT
            '========================
        ElseIf TypeOf item Is AcadMText Then

            Dim mt As Object = item

            Try : textContent = mt.TextString : Catch : End Try
            Try : height = mt.Height : Catch : End Try
            Try : rotation = mt.Rotation : Catch : End Try

            Try
                Dim pt = mt.InsertionPoint
                insertionPoint = New Point3d(pt(0), pt(1), pt(2))
            Catch
            End Try

            ' AttachmentPoint → 对齐映射
            Dim ap As Integer = 0
            Try : ap = mt.AttachmentPoint : Catch : End Try

            Select Case ap
                Case 0, 3 : hJust = 0
                Case 1, 4 : hJust = 1
                Case 2, 5 : hJust = 2
                Case Else : hJust = 0
            End Select

            Select Case ap
                Case 0, 1, 2 : vJust = 0
                Case 3, 4, 5 : vJust = 3
                Case 6, 7 : vJust = 2
            End Select

            '========================
            ' ATTRIB（块属性）
            '========================
        ElseIf TypeOf item Is AcadAttributeReference Then

            Dim at As Object = item

            Try : textContent = at.TextString : Catch : End Try
            Try : height = at.Height : Catch : End Try
            Try : rotation = at.Rotation : Catch : End Try

            Try
                Dim pt = at.InsertionPoint
                insertionPoint = New Point3d(pt(0), pt(1), pt(2))
            Catch
            End Try

            Try : hJust = at.HorizontalJustification : Catch : End Try
            Try : vJust = at.VerticalJustification : Catch : End Try

        Else
            Return Nothing
        End If

        '========================
        ' 空文本过滤
        '========================
        If String.IsNullOrWhiteSpace(textContent) Then
            Return Nothing
        End If

        '========================
        ' 创建平面（含旋转）
        '========================
        Dim plane As New Plane(insertionPoint, Vector3d.ZAxis)
        plane.Rotate(rotation, Vector3d.ZAxis)

        '========================
        ' 创建 Rhino Text
        '========================
        Dim rhinoText As New TextEntity()
        rhinoText.Plane = plane
        rhinoText.PlainText = textContent
        rhinoText.TextHeight = height

        '========================
        ' 计算边界（用于对齐）
        '========================
        Dim bbox As BoundingBox = rhinoText.GetBoundingBox(True)

        If Not bbox.IsValid Then
            bbox = New BoundingBox(Point3d.Origin, New Point3d(height, height, 0))
        End If

        Dim width As Double = bbox.Max.X - bbox.Min.X
        Dim depth As Double = bbox.Max.Y - bbox.Min.Y

        Dim offset As Vector3d = Vector3d.Zero

        '------------------------
        ' 水平对齐
        '------------------------
        Select Case hJust
            Case 1
                offset += -0.5 * width * plane.XAxis
            Case 2
                offset += -1.0 * width * plane.XAxis
        End Select

        '------------------------
        ' 垂直对齐
        '------------------------
        Select Case vJust
            Case 1
                offset += -bbox.Min.Y * plane.YAxis
            Case 2
                offset += -0.5 * (bbox.Min.Y + bbox.Max.Y) * plane.YAxis
            Case 3
                offset += -bbox.Max.Y * plane.YAxis
        End Select

        plane.Origin += offset
        rhinoText.Plane = plane

        '========================
        ' 返回
        '========================
        Return (rhinoText, layer, color, handle)

    End Function


    Private Shared Function CAD_Text2Rhino(item As Object) As (TextEntity, String, Drawing.Color, String, String, String)

        '========================
        ' 基础属性（安全获取）
        '========================
        Dim layer As String = ""
        Dim color As Drawing.Color = Drawing.Color.White
        Dim handle As String = ""

        Try : layer = item.Layer : Catch : End Try
        Try : handle = item.Handle : Catch : End Try

        Try
            color = Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)
        Catch
        End Try

        '========================
        ' 通用变量
        '========================
        Dim textContent As String = ""
        Dim insertionPoint As Point3d = Point3d.Origin
        Dim height As Double = 1.0
        Dim rotation As Double = 0
        Dim hJust As Integer = 0
        Dim vJust As Integer = 0

        '========================
        ' TEXT
        '========================
        If TypeOf item Is AcadText Then

            Dim t As Object = item

            textContent = SafeString(t.TextString)
            insertionPoint = ToPoint3d(t.InsertionPoint)
            height = SafeDouble(t.Height)
            rotation = SafeDouble(t.Rotation)

            Try : hJust = t.HorizontalJustification : Catch : End Try
            Try : vJust = t.VerticalJustification : Catch : End Try

            '========================
            ' MTEXT
            '========================
        ElseIf TypeOf item Is AcadMText Then

            Dim mt As Object = item

            textContent = CleanMText(mt.TextString) ' ⭐ 去控制符
            insertionPoint = ToPoint3d(mt.InsertionPoint)
            height = SafeDouble(mt.Height)
            rotation = SafeDouble(mt.Rotation)

            Dim ap As Integer = 0
            Try : ap = mt.AttachmentPoint : Catch : End Try

            ' AttachmentPoint → 对齐
            Select Case ap
                Case 0, 3 : hJust = 0
                Case 1, 4 : hJust = 1
                Case 2, 5 : hJust = 2
            End Select

            Select Case ap
                Case 0, 1, 2 : vJust = 0
                Case 3, 4, 5 : vJust = 3
                Case 6, 7 : vJust = 2
            End Select

            '========================
            ' ATTRIB
            '========================
        ElseIf TypeOf item Is AcadAttributeReference Then

            Dim at As Object = item

            textContent = SafeString(at.TextString)
            insertionPoint = ToPoint3d(at.InsertionPoint)
            height = SafeDouble(at.Height)
            rotation = SafeDouble(at.Rotation)

            Try : hJust = at.HorizontalJustification : Catch : End Try
            Try : vJust = at.VerticalJustification : Catch : End Try

        Else
            Return Nothing
        End If

        If String.IsNullOrWhiteSpace(textContent) Then Return Nothing

        '========================
        ' 创建 Rhino Text（核心）
        '========================
        Dim plane As New Plane(insertionPoint, Vector3d.ZAxis)
        plane.Rotate(rotation, Vector3d.ZAxis)

        Dim rhinoText As New TextEntity()
        rhinoText.Plane = plane
        rhinoText.PlainText = textContent
        rhinoText.TextHeight = height

        ' ⭐ 对齐（关键！替代 bbox）
        'rhinoText.Justification = MapJustification(hJust, vJust)
        rhinoText.Justification = RhinoTextJustification(item)
        '========================
        ' 返回
        '========================
        Return (rhinoText, layer, color, item.LineType, handle, "")

    End Function

    Private Shared Function ToPoint3d(pt As Object) As Point3d
        Try
            Return New Point3d(pt(0), pt(1), pt(2))
        Catch
            Return Point3d.Origin
        End Try
    End Function

    Private Shared Function SafeString(obj As Object) As String
        Try
            If obj Is Nothing Then Return ""
            Return obj.ToString()
        Catch
            Return ""
        End Try
    End Function

    Private Shared Function SafeDouble(obj As Object) As Double
        Try
            Return CDbl(obj)
        Catch
            Return 0.0
        End Try
    End Function
    Private Shared Function MapJustification(hJust As Integer, vJust As Integer) As TextJustification

        Select Case hJust
            Case 0 ' 左
                Select Case vJust
                    Case 3 : Return Rg.TextJustification.TopLeft
                    Case 2 : Return Rg.TextJustification.MiddleLeft
                    Case 1 : Return Rg.TextJustification.BottomLeft
                    Case Else : Return Rg.TextJustification.Left
                End Select

            Case 1 ' 中
                Select Case vJust
                    Case 3 : Return Rg.TextJustification.TopCenter
                    Case 2 : Return Rg.TextJustification.MiddleCenter
                    Case 1 : Return Rg.TextJustification.BottomCenter
                    Case Else : Return Rg.TextJustification.Center
                End Select

            Case 2 ' 右
                Select Case vJust
                    Case 3 : Return Rg.TextJustification.TopRight
                    Case 2 : Return Rg.TextJustification.MiddleRight
                    Case 1 : Return Rg.TextJustification.BottomRight
                    Case Else : Return Rg.TextJustification.Right
                End Select
        End Select

        Return Rg.TextJustification.Left
    End Function

    Private Shared Function CleanMText(input As String) As String

        If String.IsNullOrEmpty(input) Then Return ""

        Dim txt As String = input

        ' 换行
        txt = txt.Replace("\P", vbCrLf)

        ' 删除格式控制（\A \H \W \C 等）
        txt = System.Text.RegularExpressions.Regex.Replace(txt, "\\[A-Za-z0-9]+;?", "")

        ' 删除 {} 分组
        txt = txt.Replace("{", "").Replace("}", "")

        ' 删除 %% 符号（如 %%c）
        txt = System.Text.RegularExpressions.Regex.Replace(txt, "%%.", "")

        Return txt.Trim()
    End Function




    Private Shared Function CAD_Region2Rhino(item As AutoCAD.AcadRegion) As (Rg.Brep, String, Drawing.Color, String, String, String)

        '========================
        ' 1️⃣ Copy Region
        '========================
        Dim itemCopy As AutoCAD.AcadRegion = TryCast(item.Copy(), AutoCAD.AcadRegion)
        If itemCopy Is Nothing Then Return Nothing

        '========================
        ' 2️⃣ Explode
        '========================
        Dim rawObjs As Object = itemCopy.Explode()
        Dim edges As Object() = TryCast(rawObjs, Object())
        itemCopy.Delete()

        If edges Is Nothing OrElse edges.Length = 0 Then Return Nothing

        '========================
        ' 3️⃣ 转 Rhino Curve
        '========================
        Dim curves As New List(Of Rg.Curve)

        For Each edge In edges
            Try
                Dim crv As Rg.Curve = Nothing

                If TypeOf edge Is AutoCAD.AcadLine Then
                    crv = CAD_Line2Rhino_LineCurve(edge).Item1

                ElseIf TypeOf edge Is AutoCAD.AcadArc Then
                    crv = CAD_Arc2Rhino_ArcCurve(edge).Item1

                ElseIf TypeOf edge Is AutoCAD.AcadCircle Then
                    crv = CAD_Circle2Rhino(edge).Item1

                ElseIf TypeOf edge Is AutoCAD.AcadEllipse Then
                    crv = CAD_Ellipse2Rhino(edge).Item1.ToNurbsCurve()

                ElseIf TypeOf edge Is AutoCAD.AcadSpline Then
                    crv = CAD_Spline2Rhino(edge).Item1.ToNurbsCurve()

                ElseIf TypeOf edge Is AutoCAD.AcadPolyline Then
                    crv = CAD_Polyline2Rhino(edge).Item1
                End If

                If crv IsNot Nothing Then curves.Add(crv)

            Catch
            Finally
                ' 删除 COM 对象
                Try
                    edge.GetType().InvokeMember("Delete",
                    Reflection.BindingFlags.InvokeMethod, Nothing, edge, Nothing)
                Catch
                End Try
            End Try
        Next

        If curves.Count = 0 Then Return Nothing

        '========================
        ' 4️⃣ Join 曲线
        '========================
        Dim joined = Rg.Curve.JoinCurves(curves, 0.01)
        If joined Is Nothing OrElse joined.Length = 0 Then Return Nothing

        '========================
        ' 5️⃣ 过滤闭合曲线
        '========================
        Dim closedLoops = joined.Where(Function(c) c IsNot Nothing AndAlso c.IsClosed).ToList()
        If closedLoops.Count = 0 Then Return Nothing

        '========================
        ' 6️⃣ 获取真实平面（关键）
        '========================
        Dim plane As Rg.Plane
        If Not closedLoops(0).TryGetPlane(plane) Then
            plane = Rg.Plane.WorldXY
        End If

        '========================
        ' 7️⃣ 按面积排序（大→小）
        '========================
        closedLoops.Sort(Function(a, b)
                             Dim areaA = Math.Abs(Rg.AreaMassProperties.Compute(a).Area)
                             Dim areaB = Math.Abs(Rg.AreaMassProperties.Compute(b).Area)
                             Return areaB.CompareTo(areaA)
                         End Function)

        '========================
        ' 8️⃣ 外环逐个找孔
        '========================
        Dim used As New HashSet(Of Rg.Curve)
        Dim finalBreps As New List(Of Rg.Brep)

        For i = 0 To closedLoops.Count - 1

            Dim outer = closedLoops(i)
            If used.Contains(outer) Then Continue For

            Dim loops As New List(Of Rg.Curve)

            ' 外环方向（必须 CCW）
            If outer.ClosedCurveOrientation(plane) = Rg.CurveOrientation.Clockwise Then
                outer.Reverse()
            End If

            loops.Add(outer)
            used.Add(outer)

            ' 找孔
            For j = i + 1 To closedLoops.Count - 1

                Dim inner = closedLoops(j)
                If used.Contains(inner) Then Continue For

                Dim rel = Rg.Curve.PlanarClosedCurveRelationship(inner, outer, plane, 0.01)

                If rel = Rg.RegionContainment.AInsideB Then

                    ' 孔方向（必须 CW）
                    If inner.ClosedCurveOrientation(plane) = Rg.CurveOrientation.CounterClockwise Then
                        inner.Reverse()
                    End If

                    loops.Add(inner)
                    used.Add(inner)
                End If

            Next

            '========================
            ' 创建 Brep
            '========================
            Dim breps = Rg.Brep.CreatePlanarBreps(loops, 0.01)

            If breps IsNot Nothing AndAlso breps.Length > 0 Then
                finalBreps.AddRange(breps)
            End If

        Next

        If finalBreps.Count = 0 Then Return Nothing

        '========================
        ' 9️⃣ 属性
        '========================
        Dim layer As String = ""
        Dim color As Drawing.Color = Drawing.Color.White
        Dim handle As String = ""

        Try : layer = item.Layer : Catch : End Try
        Try : handle = item.Handle : Catch : End Try

        Try
            color = Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)
        Catch
        End Try

        '========================
        ' 🔟 返回
        '========================
        Return (finalBreps(0), layer, color, item.Linetype, handle, "")

    End Function




    '    Private Shared Function CAD_Ellipse2Rhino2(item As AcadEllipse) As (Rhino.Geometry.Curve, String, Drawing.Color, String, String, String)
    '        ' ============================
    '        ' 1️⃣ 基础数据
    '        ' ============================
    '        Dim c = item.Center
    '        Dim center As New Rhino.Geometry.Point3d(c(0), c(1), c(2))

    '        Dim major = item.MajorAxis
    '        Dim majorVec As New Rhino.Geometry.Vector3d(major(0), major(1), major(2))
    '        Dim majorLen As Double = majorVec.Length
    '        If majorLen < 0.000000001 Then GoTo FAIL
    '        majorVec.Unitize()

    '        Dim n = item.Normal
    '        Dim normal As New Rhino.Geometry.Vector3d(n(0), n(1), n(2))
    '        normal.Unitize()

    '        Dim ratio As Double = item.RadiusRatio
    '        Dim minorLen As Double = majorLen * ratio

    '        Dim minorVec As Rhino.Geometry.Vector3d =
    '        Rhino.Geometry.Vector3d.CrossProduct(normal, majorVec)
    '        minorVec.Unitize()
    '        minorVec *= minorLen

    '        Dim plane As New Rhino.Geometry.Plane(center, majorVec, minorVec)

    '        Dim ellipse As New Rhino.Geometry.Ellipse(plane, majorLen, minorLen)
    '        Dim nurbs As Rhino.Geometry.NurbsCurve = ellipse.ToNurbsCurve()

    '        ' ============================
    '        ' ⭐ 2️⃣ 判断完整椭圆（关键修复）
    '        ' ============================
    '        Dim startAngle As Double = item.StartAngle
    '        Dim endAngle As Double = item.EndAngle

    '        Dim isFullEllipse As Boolean =
    '        Math.Abs(endAngle - startAngle) < 0.00000001 OrElse
    '        Math.Abs(Math.Abs(endAngle - startAngle) - 2 * Math.PI) < 0.000001

    '        If isFullEllipse Then
    '            Dim layer As String = item.Layer
    '            Dim color As Drawing.Color =
    '            Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

    '            Return (nurbs, layer, color, item.Linetype, item.Handle, "")
    '        End If

    '        ' ============================
    '        ' 3️⃣ CAD 起止点
    '        ' ============================
    '        Dim sp = item.StartPoint
    '        Dim ep = item.EndPoint

    '        Dim ptStart As New Rhino.Geometry.Point3d(sp(0), sp(1), sp(2))
    '        Dim ptEnd As New Rhino.Geometry.Point3d(ep(0), ep(1), ep(2))

    '        Dim line As New Rhino.Geometry.LineCurve(ptStart, ptEnd)

    '        ' ============================
    '        ' 4️⃣ Shatter（GH）
    '        ' ============================
    '        Dim shatter_info = rd.Components.FindComponent("Shatter")
    '        If shatter_info Is Nothing Then GoTo FAIL

    '        Dim shatterDel = TryCast(shatter_info.Delegate, [Delegate])
    '        If shatterDel Is Nothing Then GoTo FAIL

    '        ' 计算tA和tB：通过起始角度和终止角度来获取
    '        Dim tList As New List(Of Double)
    '        tList.Add(startAngle / (2 * Math.PI)) ' 转换成0到1之间的值
    '        tList.Add(endAngle / (2 * Math.PI)) ' 转换成0到1之间的值
    '        tList.Sort()

    '        ' 使用Shatter组件进行分割
    '        Dim segObj As Object = Nothing
    '        Try
    '            segObj = shatterDel.DynamicInvoke(nurbs, tList)
    '        Catch
    '            GoTo FAIL
    '        End Try

    '        ' ============================
    '        ' 5️⃣ 解析曲线（修复关键）
    '        ' ============================
    '        Dim segList As New List(Of Rhino.Geometry.Curve)

    '        Dim rawSeg = TryCast(segObj, System.Collections.IEnumerable)
    '        If rawSeg IsNot Nothing Then
    '            For Each obj In rawSeg
    '                Dim cseg = TryCast(obj, Rhino.Geometry.Curve)
    '                If cseg IsNot Nothing Then segList.Add(cseg)
    '            Next
    '        End If

    '        If segList.Count = 0 Then GoTo FAIL

    '        ' ============================
    '        ' 6️⃣ 选择正确的弧段
    '        ' ============================
    '        Dim targetCurve As Rhino.Geometry.Curve = Nothing

    '        For Each seg In segList
    '            Dim midParam = seg.Domain.Mid
    '            Dim midPt = seg.PointAt(midParam)

    '            Dim v = midPt - center

    '            Dim x = Rhino.Geometry.Vector3d.Multiply(v, majorVec)
    '            Dim y = Rhino.Geometry.Vector3d.Multiply(v, minorVec) / minorLen

    '            Dim t As Double = Math.Atan2(y, x)
    '            If t < 0 Then t += 2 * Math.PI

    '            Dim inside As Boolean

    '            If startAngle <= endAngle Then
    '                inside = (t >= startAngle AndAlso t <= endAngle)
    '            Else
    '                inside = (t >= startAngle OrElse t <= endAngle)
    '            End If

    '            If inside Then
    '                targetCurve = seg
    '                Exit For
    '            End If
    '        Next

    '        If targetCurve Is Nothing Then GoTo FAIL

    '        ' ============================
    '        ' 7️⃣ 输出
    '        ' ============================
    '        Dim layerOut As String = item.Layer
    '        Dim colorOut As Drawing.Color =
    '        Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

    '        Return (targetCurve, layerOut, colorOut, item.Linetype, item.Handle, "")

    '        ' ============================
    '        ' FAIL（兜底）
    '        ' ============================
    'FAIL:
    '        Dim layer2 As String = item.Layer
    '        Dim color2 As Drawing.Color =
    '        Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

    '        Return (Nothing, layer2, color2, item.Linetype, item.Handle, "")
    '    End Function









    '    Private Shared Function CAD_Ellipse2Rhino2(item As AcadEllipse) As (Rhino.Geometry.Curve, String, Drawing.Color, String, String, String)

    '        '============================
    '        ' 1️⃣ 基础数据
    '        '============================
    '        Dim c = item.Center
    '        Dim center As New Rhino.Geometry.Point3d(c(0), c(1), c(2))

    '        Dim major = item.MajorAxis
    '        Dim majorVec As New Rhino.Geometry.Vector3d(major(0), major(1), major(2))
    '        Dim majorLen As Double = majorVec.Length
    '        If majorLen < 0.000000001 Then GoTo FAIL
    '        majorVec.Unitize()

    '        Dim n = item.Normal
    '        Dim normal As New Rhino.Geometry.Vector3d(n(0), n(1), n(2))
    '        normal.Unitize()

    '        Dim ratio As Double = item.RadiusRatio
    '        Dim minorLen As Double = majorLen * ratio

    '        Dim minorVec As Rhino.Geometry.Vector3d =
    '        Rhino.Geometry.Vector3d.CrossProduct(normal, majorVec)
    '        minorVec.Unitize()
    '        minorVec *= minorLen

    '        Dim plane As New Rhino.Geometry.Plane(center, majorVec, minorVec)

    '        Dim ellipse As New Rhino.Geometry.Ellipse(plane, majorLen, minorLen)
    '        Dim nurbs As Rhino.Geometry.NurbsCurve = ellipse.ToNurbsCurve()

    '        '============================
    '        ' ⭐ 2️⃣ 判断完整椭圆（关键修复）
    '        '============================
    '        Dim startAngle As Double = item.StartAngle
    '        Dim endAngle As Double = item.EndAngle

    '        Dim isFullEllipse As Boolean =
    '        Math.Abs(endAngle - startAngle) < 0.00000001 OrElse
    '        Math.Abs(Math.Abs(endAngle - startAngle) - 2 * Math.PI) < 0.000001

    '        If isFullEllipse Then
    '            Dim layer As String = item.Layer
    '            Dim color As Drawing.Color =
    '            Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

    '            Return (nurbs, layer, color, item.Linetype, item.Handle, "")
    '        End If

    '        '============================
    '        ' 3️⃣ CAD 起止点
    '        '============================
    '        Dim sp = item.StartPoint
    '        Dim ep = item.EndPoint

    '        Dim ptStart As New Rhino.Geometry.Point3d(sp(0), sp(1), sp(2))
    '        Dim ptEnd As New Rhino.Geometry.Point3d(ep(0), ep(1), ep(2))

    '        Dim line As New Rhino.Geometry.LineCurve(ptStart, ptEnd)

    '        '============================
    '        ' 4️⃣ Curve|Curve（GH）
    '        '============================
    '        Dim func_info = rd.Components.FindComponent("Curve|Curve")
    '        If func_info Is Nothing Then func_info = rd.Components.FindComponent("CCX")
    '        If func_info Is Nothing Then GoTo FAIL

    '        Dim funcDel = TryCast(func_info.Delegate, [Delegate])
    '        If funcDel Is Nothing Then GoTo FAIL

    '        Dim resultObj As Object = Nothing
    '        Try
    '            resultObj = funcDel.DynamicInvoke(nurbs, line)
    '        Catch
    '            GoTo FAIL
    '        End Try

    '        Dim arr = TryCast(resultObj, Object())
    '        If arr Is Nothing OrElse arr.Length < 3 Then GoTo FAIL

    '        '============================
    '        ' 5️⃣ 提取 tA
    '        '============================
    '        Dim tList As New List(Of Double)
    '        Dim rawTA = TryCast(arr(1), System.Collections.IEnumerable)

    '        If rawTA IsNot Nothing Then
    '            For Each obj In rawTA
    '                tList.Add(CDbl(obj))
    '            Next
    '        End If

    '        If tList.Count < 2 Then GoTo FAIL

    '        tList.Sort()

    '        '============================
    '        ' 6️⃣ Shatter（GH）
    '        '============================
    '        Dim shatter_info = rd.Components.FindComponent("Shatter")
    '        If shatter_info Is Nothing Then GoTo FAIL

    '        Dim shatterDel = TryCast(shatter_info.Delegate, [Delegate])
    '        If shatterDel Is Nothing Then GoTo FAIL

    '        Dim segObj As Object = Nothing
    '        Try
    '            segObj = shatterDel.DynamicInvoke(nurbs, tList)
    '        Catch
    '            GoTo FAIL
    '        End Try

    '        '============================
    '        ' 7️⃣ 解析曲线（修复关键）
    '        '============================
    '        Dim segList As New List(Of Rhino.Geometry.Curve)

    '        Dim rawSeg = TryCast(segObj, System.Collections.IEnumerable)
    '        If rawSeg IsNot Nothing Then
    '            For Each obj In rawSeg
    '                Dim cseg = TryCast(obj, Rhino.Geometry.Curve)
    '                If cseg IsNot Nothing Then segList.Add(cseg)
    '            Next
    '        End If

    '        If segList.Count = 0 Then GoTo FAIL

    '        '============================
    '        ' 8️⃣ 自动选正确弧段（核心）
    '        '============================
    '        If startAngle < 0 Then startAngle += 2 * Math.PI
    '        If endAngle < 0 Then endAngle += 2 * Math.PI

    '        Dim targetCurve As Rhino.Geometry.Curve = Nothing

    '        For Each seg In segList

    '            Dim midParam = seg.Domain.Mid
    '            Dim midPt = seg.PointAt(midParam)

    '            Dim v = midPt - center

    '            Dim x = Rhino.Geometry.Vector3d.Multiply(v, majorVec)
    '            Dim y = Rhino.Geometry.Vector3d.Multiply(v, minorVec) / minorLen

    '            Dim t As Double = Math.Atan2(y, x)
    '            If t < 0 Then t += 2 * Math.PI

    '            Dim inside As Boolean

    '            If startAngle <= endAngle Then
    '                inside = (t >= startAngle AndAlso t <= endAngle)
    '            Else
    '                inside = (t >= startAngle OrElse t <= endAngle)
    '            End If

    '            If inside Then
    '                targetCurve = seg
    '                Exit For
    '            End If

    '        Next

    '        If targetCurve Is Nothing Then GoTo FAIL

    '        '============================
    '        ' 9️⃣ 输出
    '        '============================
    '        Dim layerOut As String = item.Layer
    '        Dim colorOut As Drawing.Color =
    '        Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

    '        Return (targetCurve, layerOut, colorOut, item.Linetype, item.Handle, "")

    '        '============================
    '        ' FAIL（兜底）
    '        '============================
    'FAIL:
    '        Dim layer2 As String = item.Layer
    '        Dim color2 As Drawing.Color =
    '        Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

    '        Return (Nothing, layer2, color2, item.Linetype, item.Handle, "")

    '    End Function








    '    Private Shared Function CAD_Ellipse2Rhino(item As AcadEllipse) _
    'As (Rhino.Geometry.Curve, String, Drawing.Color, String, String, String)

    '        '=========================
    '        ' 1. 提取CAD属性
    '        '=========================
    '        Dim layerName As String = item.Layer
    '        Dim linetypeName As String = item.Linetype
    '        Dim handle As String = item.Handle

    '        '=========================
    '        ' 2. 颜色转换
    '        '=========================
    '        Dim cadColor As AcadAcCmColor = item.TrueColor
    '        Dim convertColor As Drawing.Color = Drawing.Color.FromArgb(cadColor.Red, cadColor.Green, cadColor.Blue)

    '        '=========================
    '        ' 3. 获取椭圆几何参数
    '        '=========================
    '        Dim centerPt As Double() = item.Center
    '        Dim center As New Point3d(centerPt(0), centerPt(1), centerPt(2))

    '        Dim majorAxisVec As Double() = item.MajorAxis
    '        Dim majorAxis As New Vector3d(majorAxisVec(0), majorAxisVec(1), majorAxisVec(2))
    '        majorAxis.Unitize()

    '        ' 计算短轴（垂直于长轴与Z轴）
    '        Dim minorAxis As Vector3d = Vector3d.CrossProduct(Vector3d.ZAxis, majorAxis)
    '        Dim radMajor As Double = item.MajorRadius
    '        Dim radMinor As Double = item.MinorRadius

    '        '=========================
    '        ' 4. 创建椭圆平面与椭圆
    '        '=========================
    '        Dim ellipsePlane As New Rhino.Geometry.Plane(center, majorAxis, minorAxis)
    '        Dim rhinoEllipse As New Rhino.Geometry.Ellipse(ellipsePlane, radMajor, radMinor)

    '        ' 转NURBS曲线
    '        Dim baseCurve As Rhino.Geometry.NurbsCurve = rhinoEllipse.ToNurbsCurve()
    '        Dim resultCurve As Rhino.Geometry.Curve = Nothing

    '        '=========================
    '        ' 5. 处理完整椭圆 / 椭圆弧（正确Trim重载）
    '        '=========================
    '        Const tol As Double = 0.000001 ' 浮点误差容忍

    '        ' 判断是否为完整椭圆
    '        If Math.Abs(item.StartAngle) < tol AndAlso Math.Abs(item.EndAngle - 2 * Math.PI) < tol Then
    '            resultCurve = baseCurve
    '        Else
    '            ' 使用 Rhino 正确重载：Trim(t0 As Double, t1 As Double)
    '            ' 椭圆的参数域刚好对应弧度角度，直接传入起始角、终止角即可
    '            resultCurve = baseCurve.Trim(item.StartAngle, item.EndAngle)
    '        End If

    '        '=========================
    '        ' 6. 有效性校验
    '        '=========================
    '        If resultCurve Is Nothing OrElse Not resultCurve.IsValid Then
    '            Throw New InvalidOperationException("椭圆转换失败：生成曲线无效")
    '        End If

    '        '=========================
    '        ' 7. 返回要求的6个值
    '        '=========================
    '        Return (resultCurve, layerName, convertColor, linetypeName, handle, "")

    '    End Function

    Private Shared Function CAD_Ellipse2Rhino(item As AcadEllipse) As (Rhino.Geometry.Curve, String, Drawing.Color, String, String, String)
        ' ============================
        ' 1️⃣ 基础数据
        ' ============================
        Dim c = item.Center
        Dim center As New Rhino.Geometry.Point3d(c(0), c(1), c(2))

        Dim major = item.MajorAxis
        Dim majorVec As New Rhino.Geometry.Vector3d(major(0), major(1), major(2))
        Dim majorLen As Double = majorVec.Length
        If majorLen < 0.000000001 Then GoTo FAIL
        majorVec.Unitize()

        Dim n = item.Normal
        Dim normal As New Rhino.Geometry.Vector3d(n(0), n(1), n(2))
        normal.Unitize()

        Dim ratio As Double = item.RadiusRatio
        Dim minorLen As Double = majorLen * ratio

        Dim minorVec As Rhino.Geometry.Vector3d =
    Rhino.Geometry.Vector3d.CrossProduct(normal, majorVec)
        minorVec.Unitize()
        minorVec *= minorLen

        Dim plane As New Rhino.Geometry.Plane(center, majorVec, minorVec)

        Dim ellipse As New Rhino.Geometry.Ellipse(plane, majorLen, minorLen)
        Dim nurbs As Rhino.Geometry.NurbsCurve = ellipse.ToNurbsCurve()

        ' ============================
        ' ⭐ 2️⃣ 判断完整椭圆（关键修复）
        ' ============================
        Dim startAngle As Double = item.StartAngle
        Dim endAngle As Double = item.EndAngle
        If startAngle > 2 * Math.PI Then
            startAngle = startAngle - 2 * Math.PI
        End If
        If endAngle > 2 * Math.PI Then
            endAngle = endAngle - 2 * Math.PI
        End If

        Dim isFullEllipse As Boolean = Math.Abs(endAngle - startAngle) < 0.00000001 OrElse Math.Abs(Math.Abs(endAngle - startAngle) - 2 * Math.PI) < 0.000001

        If isFullEllipse Then
            Dim layer As String = item.Layer
            Dim color As Drawing.Color = Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

            Return (nurbs, layer, color, item.Linetype, item.Handle, "")
        End If

        ' ============================
        ' 3️⃣ CAD 起止点
        ' ============================
        Dim sp = item.StartPoint
        Dim ep = item.EndPoint

        Dim ptStart As New Rhino.Geometry.Point3d(sp(0), sp(1), sp(2))
        Dim ptEnd As New Rhino.Geometry.Point3d(ep(0), ep(1), ep(2))

        Dim line As New Rhino.Geometry.LineCurve(ptStart, ptEnd)

        ' ============================
        ' 4️⃣ Curve|Curve（GH）
        ' ============================
        Dim func_info = rd.Components.FindComponent("Curve|Curve")
        If func_info Is Nothing Then func_info = rd.Components.FindComponent("CCX")
        If func_info Is Nothing Then GoTo FAIL

        Dim funcDel = TryCast(func_info.Delegate, [Delegate])
        If funcDel Is Nothing Then GoTo FAIL

        Dim resultObj As Object = Nothing
        Try
            resultObj = funcDel.DynamicInvoke(nurbs, line)
        Catch
            GoTo FAIL
        End Try

        Dim arr = TryCast(resultObj, Object())
        If arr Is Nothing OrElse arr.Length < 3 Then GoTo FAIL

        ' ============================
        ' 5️⃣ 提取 tA
        ' ============================
        Dim tList As New List(Of Double)
        Dim tA = TryCast(arr(1), System.Collections.IEnumerable)

        If tA IsNot Nothing Then
            For Each obj In tA
                tList.Add(CDbl(obj))
            Next
        End If

        If tList.Count < 2 Then GoTo FAIL

        'tList.Sort()

        ' ============================
        ' 6️⃣ Shatter（GH）
        ' ============================
        Dim shatter_info = rd.Components.FindComponent("Shatter")
        If shatter_info Is Nothing Then GoTo FAIL

        Dim shatterDel = TryCast(shatter_info.Delegate, [Delegate])
        If shatterDel Is Nothing Then GoTo FAIL

        Dim segObj As Object = Nothing
        Try
            segObj = shatterDel.DynamicInvoke(nurbs, tList)
        Catch
            GoTo FAIL
        End Try

        ' ============================
        ' 7️⃣ 解析曲线（修复关键）
        ' ============================
        Dim segList As New List(Of Rhino.Geometry.Curve)

        Dim rawSeg = TryCast(segObj, System.Collections.IEnumerable)
        If rawSeg IsNot Nothing Then
            For Each obj In rawSeg
                Dim cseg = TryCast(obj, Rhino.Geometry.Curve)
                If cseg IsNot Nothing Then segList.Add(cseg)
            Next
        End If

        If segList.Count = 0 Then GoTo FAIL

        ' ============================
        ' 8️⃣ 自动选正确弧段（核心）
        ' ============================
        If startAngle < 0 Then startAngle += 2 * Math.PI
        If endAngle < 0 Then endAngle += 2 * Math.PI

        Dim targetCurve As Rhino.Geometry.Curve = Nothing

        For Each seg In segList

            Dim midParam = seg.Domain.Mid
            Dim midPt = seg.PointAt(midParam)

            Dim v = midPt - center

            Dim x = Rhino.Geometry.Vector3d.Multiply(v, majorVec)
            Dim y = Rhino.Geometry.Vector3d.Multiply(v, minorVec) / minorLen

            Dim t As Double = Math.Atan2(y, x)
            If t < 0 Then t += 2 * Math.PI

            Dim inside As Boolean

            If startAngle <= endAngle Then
                inside = (t >= startAngle AndAlso t <= endAngle)
            Else
                inside = (t >= startAngle OrElse t <= endAngle)
            End If

            If inside Then
                targetCurve = seg
                Exit For
            End If

        Next

        If targetCurve Is Nothing Then GoTo FAIL

        ' ============================
        ' 9️⃣ 输出
        ' ============================
        Dim layerOut As String = item.Layer
        Dim colorOut As Drawing.Color =
    Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

        Return (targetCurve, layerOut, colorOut, item.Linetype, item.Handle, "")

        ' ============================
        ' FAIL（兜底）
        ' ============================
FAIL:
        Dim layer2 As String = item.Layer
        Dim color2 As Drawing.Color =
    Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

        Return (Nothing, layer2, color2, item.Linetype, item.Handle, "")
    End Function





    Private Shared Function CAD_Spline2Rhino(item As AcadSpline) As (Rhino.Geometry.Curve, String, Drawing.Color, String, String, String)

        '==============================
        ' 1. 控制点
        '==============================
        Dim ctrlArr() As Double = CType(item.ControlPoints, Double())

        Dim pts As New List(Of Rhino.Geometry.Point3d)
        For i As Integer = 0 To ctrlArr.Length - 1 Step 3
            pts.Add(New Rhino.Geometry.Point3d(ctrlArr(i), ctrlArr(i + 1), ctrlArr(i + 2)))
        Next

        Dim ptCount As Integer = pts.Count

        '==============================
        ' 2. Degree
        '==============================
        Dim degree As Integer = item.Degree
        Dim order As Integer = degree + 1

        '==============================
        ' 3. 权重（是否 Rational）
        '==============================
        Dim weights() As Double = Nothing
        Dim isRational As Boolean = False

        Try
            weights = CType(item.Weights, Double())
            If weights IsNot Nothing AndAlso weights.Length = ptCount Then
                isRational = True
            End If
        Catch
            isRational = False
        End Try

        '==============================
        ' 4. Knot（AutoCAD原始）
        '==============================
        Dim acadKnots() As Double = CType(item.Knots, Double())

        '==============================
        ' 5. 构造 Rhino Nurbs
        '==============================
        Dim nurbs As New Rhino.Geometry.NurbsCurve(3, isRational, order, ptCount)

        ' 控制点 + 权重
        For i As Integer = 0 To ptCount - 1
            If isRational Then
                nurbs.Points.SetPoint(i, pts(i), weights(i))
            Else
                nurbs.Points.SetPoint(i, pts(i))
            End If
        Next

        '==============================
        ' 6. Knot 转换（核心！）
        '==============================
        ' AutoCAD: n + degree + 1
        ' Rhino:   n + degree - 1

        Dim expectedRhinoKnotCount As Integer = nurbs.Knots.Count
        Dim expectedAcadKnotCount As Integer = acadKnots.Length

        ' 👉 常见情况：需要去掉首尾重复 knot
        Dim rhinoKnots(expectedRhinoKnotCount - 1) As Double

        If expectedAcadKnotCount = expectedRhinoKnotCount + 2 Then
            ' 去掉首尾
            For i As Integer = 0 To expectedRhinoKnotCount - 1
                rhinoKnots(i) = acadKnots(i + 1)
            Next

        ElseIf expectedAcadKnotCount = expectedRhinoKnotCount Then
            ' 直接用（少见）
            rhinoKnots = acadKnots

        Else
            ' fallback：重新参数化（保险）
            Dim t0 As Double = 0.0
            Dim t1 As Double = 1.0
            For i As Integer = 0 To expectedRhinoKnotCount - 1
                rhinoKnots(i) = t0 + (t1 - t0) * i / (expectedRhinoKnotCount - 1)
            Next
        End If

        ' 赋值 Knot
        For i As Integer = 0 To nurbs.Knots.Count - 1
            nurbs.Knots(i) = rhinoKnots(i)
        Next

        '==============================
        ' 7. 周期性（可选）
        '==============================
        If item.Closed Then
            nurbs.MakeClosed(0.001)
        End If

        '==============================
        ' 8. 图层 & 颜色
        '==============================
        Dim layer As String = item.Layer

        Dim color As Drawing.Color =
            Drawing.Color.FromArgb(item.TrueColor.Red, item.TrueColor.Green, item.TrueColor.Blue)

        '==============================
        ' 9. 验证
        '==============================
        If Not nurbs.IsValid Then
            ' fallback：最保险方式（拟合）
            Dim safeCurve = Rhino.Geometry.Curve.CreateInterpolatedCurve(pts, degree)
            Return (safeCurve, layer, color, item.Linetype, item.Handle, "")
        End If

        Return (nurbs, layer, color, item.Linetype, item.Handle, "")

    End Function

    'Private Shared Function CAD_Block2Rhino(item As AutoCAD.AcadBlockReference) As List(Of (Object, String, Drawing.Color, String, String))

    '    Dim result As New List(Of (Object, String, Drawing.Color, String, String))

    '    Try
    '        '============================
    '        ' 1️⃣ Copy Block
    '        '============================
    '        Dim blkCopy As AutoCAD.AcadBlockReference =
    '            TryCast(item.Copy(), AutoCAD.AcadBlockReference)

    '        If blkCopy Is Nothing Then Return result

    '        '============================
    '        ' 2️⃣ Explode
    '        '============================
    '        Dim exploded As Object = blkCopy.Explode()
    '        Dim objs As System.Collections.IEnumerable =
    '            TryCast(exploded, System.Collections.IEnumerable)

    '        If objs Is Nothing Then
    '            blkCopy.Delete()
    '            Return result
    '        End If

    '        '============================
    '        ' 3️⃣ 遍历
    '        '============================
    '        For Each obj In objs

    '            '============================
    '            ' ❗ 3.1 嵌套块 → 跳过
    '            '============================
    '            If TypeOf obj Is AutoCAD.AcadBlockReference Then

    '                Rhino.RhinoApp.WriteLine("⚠ Nested block skipped.")

    '                ' 删除临时对象
    '                Try : obj.Delete() : Catch : End Try

    '                Continue For
    '            End If

    '            '============================
    '            ' 3.2 转换
    '            '============================
    '            Try

    '                If TypeOf obj Is AutoCAD.AcadLine Then
    '                    result.Add(CAD_Line2Rhino_LineCurve(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadCircle Then
    '                    Dim r = CAD_Circle2Rhino(obj)
    '                    result.Add((r.Item1, r.Item2, r.Item3, r.Item4, r.Item5))

    '                ElseIf TypeOf obj Is AutoCAD.AcadArc Then
    '                    result.Add(CAD_Arc2Rhino_ArcCurve(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadLWPolyline Then
    '                    result.Add(CAD_Polyline2Rhino(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadEllipse Then
    '                    result.Add(CAD_Ellipse2Rhino(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadSpline Then
    '                    result.Add(CAD_Spline2Rhino(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadRegion Then
    '                    result.Add(CAD_Region2Rhino(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadText OrElse
    '                       TypeOf obj Is AutoCAD.AcadMText Then

    '                    result.Add(CAD_Text2Rhino(obj))

    '                ElseIf TypeOf obj Is AutoCAD.AcadPoint Then
    '                    result.Add(CAD_Point2Rhino(obj))

    '                Else
    '                    Rhino.RhinoApp.WriteLine($"⚠ Unsupported type: {obj.GetType().Name}")
    '                End If

    '            Catch ex As Exception
    '                Rhino.RhinoApp.WriteLine($"⚠ Convert failed: {ex.Message}")
    '            End Try

    '            '============================
    '            ' 3.3 删除临时对象
    '            '============================
    '            Try
    '                Dim m = obj.GetType().GetMethod("Delete")
    '                If m IsNot Nothing Then m.Invoke(obj, Nothing)
    '            Catch
    '            End Try

    '        Next

    '        '============================
    '        ' 4️⃣ 删除复制块
    '        '============================
    '        blkCopy.Delete()

    '    Catch ex As Exception
    '        Rhino.RhinoApp.WriteLine("Block convert error: " & ex.Message)
    '    End Try

    '    Return result

    'End Function

    Private Shared Function CAD_Block2Rhino_RegionOnly2(item As AutoCAD.AcadBlockReference) As (Object, String, Drawing.Color, String, String, String)

        Dim doc As AutoCAD.AcadDocument = item.Document

        ' 返回字段说明：
        ' Item1 → 几何（GeometryBase）
        ' Item2 → 图层
        ' Item3 → 颜色
        ' Item4 → 线型
        ' Item5 → Handle
        ' Item6 → 块名

        Try
            '============================
            ' ⭐ 块名
            '============================
            Dim blockName As String = item.Name

            '============================
            ' ⭐ 1️⃣ Undo 隔离开始
            '============================
            doc.StartUndoMark()

            '============================
            ' 2️⃣ 复制块（避免污染原图）
            '============================
            Dim blkCopy As AutoCAD.AcadBlockReference =
            TryCast(item.Copy(), AutoCAD.AcadBlockReference)

            If blkCopy Is Nothing Then GoTo FAIL

            '============================
            ' 3️⃣ 炸开
            '============================
            Dim exploded As Object = blkCopy.Explode()

            Dim objs As System.Collections.IEnumerable =
            TryCast(exploded, System.Collections.IEnumerable)

            If objs Is Nothing Then GoTo FAIL

            '============================
            ' 4️⃣ 查找 Region（忽略嵌套块）
            '============================
            For Each obj In objs

                ' 跳过嵌套块（按你的要求：只警告/忽略）
                If TypeOf obj Is AutoCAD.AcadBlockReference Then
                    Rhino.RhinoApp.WriteLine("Warning: Nested block found in [" & blockName & "]")
                    Continue For
                End If

                ' 找到 Region
                If TypeOf obj Is AutoCAD.AcadRegion Then

                    Dim r = CAD_Region2Rhino(obj)

                    '============================
                    ' ⭐ 5️⃣ Undo 回滚（关键）
                    '============================
                    doc.EndUndoMark()
                    doc.SendCommand("_.UNDO _BACK " & vbCr)

                    '============================
                    ' 返回（块名在最后）
                    '============================
                    Return (r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, blockName)

                End If

            Next

        Catch ex As Exception
            Rhino.RhinoApp.WriteLine("CAD_Block2Rhino_RegionOnly Error: " & ex.Message)
        End Try

FAIL:
        '============================
        ' ⭐ 保底回滚（防止残留）
        '============================
        Try
            doc.EndUndoMark()
            doc.SendCommand("_.UNDO _BACK " & vbCr)
        Catch
        End Try

        Return (Nothing, "", Drawing.Color.White, "", "", "")

    End Function


    Private Shared Function CAD_Block2Rhino_RegionOnly3(item As AutoCAD.AcadBlockReference) As (Object, String, Drawing.Color, String, String, String)

        Dim doc As AutoCAD.AcadDocument = item.Document
        Dim app = doc.Application

        '============================
        ' ⭐ 保存原始状态
        '============================
        Dim oldEcho As Object = doc.GetVariable("CMDECHO")
        Dim oldRegen As Object = doc.GetVariable("REGENMODE")

        Try
            '============================
            ' ⭐ 关闭刷新 + 静默执行
            '============================
            app.ScreenUpdating = False
            doc.SetVariable("CMDECHO", 0)
            doc.SetVariable("REGENMODE", 0)

            '============================
            ' ⭐ 块名
            '============================
            Dim blockName As String = item.Name

            '============================
            ' ⭐ Undo 隔离
            '============================
            doc.StartUndoMark()

            '============================
            ' 复制块
            '============================
            Dim blkCopy As AutoCAD.AcadBlockReference =
                TryCast(item.Copy(), AutoCAD.AcadBlockReference)

            If blkCopy Is Nothing Then GoTo FAIL

            '============================
            ' 炸开
            '============================
            Dim exploded As Object = blkCopy.Explode()

            Dim objs As System.Collections.IEnumerable =
                TryCast(exploded, System.Collections.IEnumerable)

            If objs Is Nothing Then GoTo FAIL

            '============================
            ' 查找 Region
            '============================
            For Each obj In objs

                ' 忽略嵌套块（不再输出到Rhino）
                If TypeOf obj Is AutoCAD.AcadBlockReference Then
                    Continue For
                End If

                If TypeOf obj Is AutoCAD.AcadRegion Then

                    Dim r = CAD_Region2Rhino(obj)

                    '============================
                    ' Undo 回滚
                    '============================
                    doc.EndUndoMark()
                    doc.SendCommand("_.UNDO _BACK " & vbCr)

                    Return (r.Item1, r.Item2, r.Item3, r.Item4, r.Item5, blockName)

                End If

            Next

        Catch ex As Exception
            ' ❗ 不再写 Rhino 命令行（你要求的）
            ' 可以后续改成 GH message
        End Try

FAIL:
        Try
            doc.EndUndoMark()
            doc.SendCommand("_.UNDO _BACK " & vbCr)
        Catch
        End Try

        '============================
        ' ⭐ 恢复状态（必须执行）
        '============================
        Try
            app.ScreenUpdating = True
            doc.SetVariable("CMDECHO", oldEcho)
            doc.SetVariable("REGENMODE", oldRegen)
            doc.Regen(AutoCAD.AcRegenType.acActiveViewport)
        Catch
        End Try

        Return (Nothing, "", Drawing.Color.White, "", "", "")

    End Function

    Private Shared Function CAD_Block2Rhino_RegionOnly(item As AutoCAD.AcadBlockReference) As (Object, String, Drawing.Color, String, String, String)

        ' 返回： 
        ' Item1 → 几何
        ' Item2 → 图层
        ' Item3 → 颜色
        ' Item4 → 线型
        ' Item5 → Handle
        ' Item6 → 块名

        Dim blockName As String = item.Name

        Try
            '============================
            ' 1️⃣ Copy（避免污染原始块）
            '============================
            Dim blkCopy As AutoCAD.AcadBlockReference = TryCast(item.Copy(), AutoCAD.AcadBlockReference)

            If blkCopy Is Nothing Then GoTo FAIL

            '============================
            ' 2️⃣ Explode（直接拿 list）
            '============================
            Dim list As Object = blkCopy.Explode()

            Dim result As (Object, String, Drawing.Color, String, String, String) = Nothing

            '============================
            ' 3️⃣ 查找 Region
            '============================
            For Each obj In list
                If TypeOf obj Is AutoCAD.AcadRegion Then
                    ' 捕获可能抛出的异常
                    Try
                        result = CAD_Region2Rhino(obj)
                        ' 如果找到了有效的结果，跳出循环
                        If result.Item1 IsNot Nothing Then
                            Exit For
                        End If
                    Catch ex As Exception
                        ' 如果 CAD_Region2Rhino 出现异常，跳过此 Region，并继续处理其他对象
                        ' 可以记录异常信息，或者采取其他措施
                        Continue For
                    End Try
                End If
            Next

            '============================
            ' 4️⃣ 删除炸开对象（确保清理）
            '============================
            For Each obj In list
                Dim ent = TryCast(obj, AutoCAD.AcadEntity)
                If ent IsNot Nothing Then
                    Try
                        ent.Delete()
                    Catch ex As Exception
                        ' 这里捕获异常以确保删除的对象不会中断后续操作
                        ' 你可以在这里记录日志，或者做其他处理
                    End Try
                End If
            Next

            '============================
            ' 5️⃣ 删除块副本
            '============================
            Try
                blkCopy.Delete()
            Catch ex As Exception
                ' 同样，如果删除块副本时出错，捕获异常，避免阻断
                ' 你可以在这里记录日志，或者做其他处理
            End Try

            '============================
            ' 6️⃣ 返回
            '============================
            If result.Item1 IsNot Nothing Then
                Return (result.Item1, result.Item2, result.Item3, result.Item4, result.Item5, blockName)
            End If

        Catch ex As Exception
            ' 如果有任何异常，跳过，后续的清理工作会继续执行
            ' 可选：后续改 GH message
        End Try

FAIL:
        Return (Nothing, "", Drawing.Color.White, "", "", "")

    End Function



End Class





