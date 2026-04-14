Option Explicit On
Imports AutoCAD

Public Class MyAutoCADTool
    Private Declare Function FindWindow Lib "user32.dll" Alias "FindWindowA" (ByVal lpClassName As String, ByVal lpWindowName As String) As IntPtr
    Private Declare Function SetForegroundWindow Lib "user32.dll" (ByVal hwnd As IntPtr) As IntPtr
    ''' <summary>
    ''' CAD程序的实例
    ''' </summary>
    ''' <remarks></remarks>
    Public TAcadApp As AcadApplication
    ''' <summary>
    ''' CAD文档的指针
    ''' </summary>
    ''' <remarks></remarks>
    Private iAcadDoc As AcadDocument
    Public MaxCount As Integer = 300000 '最大选择集数量


    Public ReadOnly Property AcadApp As AcadApplication
        Get
            If TAcadApp Is Nothing Then
                ConnectCAD()
            End If
            Return TAcadApp
        End Get
    End Property






    'Private activeSheet As Worksheet
    ' 可创建的 COM 类必须具有一个不带参数的 Public Sub New() 
    ' 否则， 将不会在 
    ' COM 注册表中注册此类，且无法通过
    ' CreateObject 创建此类。
    Public Sub New()
        MyBase.New()
    End Sub


    ''' <summary>
    ''' 新建一个cad文档
    ''' </summary>
    Public Sub Add_CAD_NewDocument()
        TAcadApp.Documents.Add()
    End Sub





    ''' <summary>
    ''' 在屏幕上选择元素，元素类型由TypeOfSelectObject指定
    ''' entityType是CAD内部定义的组码
    ''' </summary>
    ''' <param name="filterCode"></param>过滤器类型的组码
    ''' <param name="filterStr"></param>实体类型的组码
    ''' <returns></returns>
    Public Function GetSelectionSetOnScreen(filterCode As Integer, filterStr As String) As AcadSelectionSet
        Dim ssetObj As AcadSelectionSet
        On Error Resume Next
        iAcadDoc.SelectionSets().Item("sset_object").Delete()
        ssetObj = iAcadDoc.SelectionSets().Add("sset_object")
        On Error GoTo 0
        If Not (ssetObj Is Nothing) Then
            Me.MySetForegroundWindow(TAcadApp.Caption()) '将CAD窗口前置
            Dim FilterType(0 To 0) As Int16  '注意VBA中的integer类型在VB.Net中对应的是int16
            Dim FilterData(0 To 0) As Object
            FilterType(0) = filterCode
            FilterData(0) = filterStr
            ssetObj.SelectOnScreen(FilterType, FilterData)
            If ssetObj.Count = 0 Then
                ssetObj = Nothing
            End If

            '如果超过最大试用次数，就将多的从选择集中去掉
            If ssetObj.Count > MaxCount Then
                Dim i As Integer
                For i = MaxCount To ssetObj.Count
                    ssetObj.RemoveItems(i)
                Next
            End If
        End If
        Return ssetObj
    End Function


    ''' <summary>
    ''' 从屏幕获取选择集，对象类型为直线和文本
    ''' </summary>
    ''' <returns></returns>
    Public Function GetSelectionSetOnScreen_TextAndLine() As AcadSelectionSet
        Dim ssetObj As AcadSelectionSet
        On Error Resume Next
        iAcadDoc.SelectionSets().Item("sset_object").Delete()
        ssetObj = iAcadDoc.SelectionSets().Add("sset_object")
        If ssetObj IsNot Nothing Then
            Me.MySetForegroundWindow(TAcadApp.Caption()) '将CAD窗口前置
            Dim FilterType(0 To 3) As Int16  '注意VBA中的integer类型在VB.Net中对应的是int16
            Dim FilterData(0 To 3) As Object
            '设置过滤器类型 
            FilterType(0) = -4
            FilterType(1) = 0
            FilterType(2) = 0
            FilterType(3) = -4
            '设置过滤数据
            FilterData(0) = "<or"
            FilterData(1) = "LINE"
            FilterData(2) = "TEXT"
            FilterData(3) = "or>"

            ssetObj.SelectOnScreen(FilterType, FilterData)
            If (ssetObj.Count = 0) Then
                ssetObj = Nothing
            End If

            '如果超过最大试用次数，就将多的从选择集中去掉
            If ssetObj.Count > MaxCount Then
                Dim i As Integer
                For i = MaxCount To ssetObj.Count
                    ssetObj.RemoveItems(i)
                Next
            End If
        End If
        Return ssetObj
    End Function



    '''<summary>
    ''' 连接CAD
    ''' </summary>
    ''' <returns>连接成功返回true，否则返回false</returns>
    ''' <remarks></remarks>
    Public Function ConnectCAD() As Boolean
        On Error Resume Next
        TAcadApp = GetObject(, "AutoCAD.Application")
        If TAcadApp Is Nothing Then
            On Error Resume Next
            TAcadApp = CreateObject("AutoCAD.Application", )
            If TAcadApp Is Nothing Then
                MsgBox("无法连接或打开AutoCAD，请检查AutoCAD是否安装正确！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
                Return False
            End If
        End If
        TAcadApp.Visible = True
        Return True
    End Function



    Private Function CheckCadOK() As Boolean
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
    Private Function CheckModelSpaceIsActivety() As Boolean
        If iAcadDoc.GetVariable("CTAB") = "Model" Then
            CheckModelSpaceIsActivety = True
        Else
            CheckModelSpaceIsActivety = False
        End If
    End Function


    Public Sub MySetForegroundWindow(ByVal caption As String)
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

















































End Class




