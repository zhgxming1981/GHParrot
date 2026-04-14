
Imports Microsoft.Office.Interop.Excel

Public Class MyExcel
    Protected iExcelApp As Microsoft.Office.Interop.Excel.Application '指向当前excel程序的指针
    Protected iWorkbook As Microsoft.Office.Interop.Excel.Workbook '指向当前的excel工作簿的指针
    Private iCurrentRow As Integer = -1 '当前的行
    Private iCurrentColumn As Integer = -1 '当前的列


    ''' <summary>
    ''' 连接Excel程序，成功返回true，不成功返回false
    ''' </summary>
    ''' <returns></returns>
    Public Function ConnectExcel() As Boolean
        On Error Resume Next
        iExcelApp = GetObject(, "Excel.Application")
        If iExcelApp Is Nothing Then
            On Error Resume Next
            iExcelApp = CreateObject("Excel.Application",)
            If iExcelApp Is Nothing Then
                MsgBox("无法连接或打开Excel，请检查Excel是否安装正确！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
                Return False
            End If
        End If
        iExcelApp.Visible = True
        Return True
    End Function

    ''' <summary>
    ''' 如果已经连接过Excel，则跳过，否则调用ConnectExcel
    ''' </summary>
    ''' <returns></returns>
    Public Function GetExcel() As Boolean
        Dim retval As Boolean
        If iExcelApp IsNot Nothing Then
            retval = True
        Else
            retval = ConnectExcel()
        End If
        If retval Then
            iExcelApp.Visible = True
        End If
        Return retval
    End Function



    Public ReadOnly Property MyWorkBooks As Microsoft.Office.Interop.Excel.Workbooks
        Get
            Return iExcelApp.Workbooks
        End Get
    End Property

    Public ReadOnly Property MyWorkbook As Microsoft.Office.Interop.Excel.Workbook
        Get
            If iWorkbook Is Nothing Then
                iWorkbook = iExcelApp.Workbooks.Add()
            End If
            Return iWorkbook
        End Get
    End Property

    Public ReadOnly Property ActiveSheet As Worksheet
        Get
            If iExcelApp IsNot Nothing Then
                Return iExcelApp.ActiveSheet
            End If
            Return Nothing
        End Get
    End Property


    ''' <summary>
    ''' 通过名称获取workbook引用
    ''' </summary>
    ''' <param name="name"></param>workbook名称
    ''' <returns></returns>
    Public Function GetWorkBook(Optional name As String = "新建工作簿…") As Workbook
        If iExcelApp Is Nothing Then
            MsgBox("尚未连接Excel！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
            Return Nothing
        End If
        On Error Resume Next
        If name = "新建工作簿…" Then
            iWorkbook = iExcelApp.Workbooks.Add()
        Else
            iWorkbook = iExcelApp.Workbooks(name)
        End If
        iWorkbook.Activate()
        On Error GoTo 0
        Return iWorkbook
    End Function

    ''' <summary>
    ''' 打开名为fileName的Excel文件
    ''' </summary>
    ''' <param name="fileName"></param>
    ''' <returns></returns>
    Public Function OpenExcelFile(fileName As String) As Workbook
        If iExcelApp Is Nothing Then
            ConnectExcel()
        End If

        'Dim OpenBook As Workbook
        iWorkbook = iExcelApp.Workbooks.Open(fileName)
        Return iWorkbook
    End Function


    Public Sub SaveAsExcelFile(fileName As String)
        'If iExcelApp Is Nothing Then
        '    ConnectExcel()
        'End If

        iWorkbook.SaveAs(fileName)
    End Sub


    Public Sub SaveExcelFile()
        iWorkbook.Save()
    End Sub

    Public Sub NewExcelFile()
        If iExcelApp Is Nothing Then
            ConnectExcel()
        End If
        iWorkbook = iExcelApp.Workbooks.Add()
    End Sub

    ''' <summary>
    ''' 恢复Excel为正常状态
    ''' </summary>
    Public Sub SetExcelWindowState(visible As Boolean, update As Boolean, displayAlert As Boolean)
        If iExcelApp IsNot Nothing Then
            iExcelApp.Visible = visible
            iExcelApp.ScreenUpdating = update
            iExcelApp.DisplayAlerts = displayAlert
        End If
    End Sub



    Public Sub SetActiveSheet(sheetName As String)
        Dim sheet As Worksheet
        Dim IsExist = False
        For Each sheet In iWorkbook.Sheets
            If sheet.Name = sheetName Then
                sheet.Activate()
                IsExist = True
            End If
        Next
        If IsExist = False Then
            Dim newSheet As Worksheet = iWorkbook.Sheets.Add()
            newSheet.Name = sheetName
            newSheet.Activate()
        End If
    End Sub







    'Public Sub WriteData(rowIndex As Integer, colIndex As Integer, data As Object)
    '    'iCurrentRow = rowIndex
    '    'iCurrentColumn = colIndex
    '    ActiveSheet.Cells(rowIndex, colIndex).Value = data
    'End Sub


    Public Sub WriteData(rowIndex As Integer, colIndex As Integer, data As List(Of List(Of String)))
        Dim lenRow = data.Count
        Dim i, j As Integer
        For i = 0 To lenRow - 1
            Dim lenCol = data(i).Count
            For j = 0 To lenCol - 1
                ActiveSheet.Cells(rowIndex + i, colIndex + j).Value = data(i)(j)
            Next
        Next
    End Sub

    Private Function CheckExcelOK() As Boolean
        If iWorkbook Is Nothing Then
            MsgBox("尚未连接任何WorkBook！", MsgBoxStyle.MsgBoxSetForeground + MsgBoxStyle.OkOnly + MsgBoxStyle.SystemModal + MsgBoxStyle.Exclamation, "错误信息")
            Return False
        End If
        Return True
    End Function

    Public Function Row(cell As String) As Integer
        Return ActiveSheet.Range(cell).Row
    End Function

    Public Function Column(cell As String) As Integer
        Return ActiveSheet.Range(cell).Column
    End Function

    Public Sub CloseExcelFile()
        iWorkbook.Close()
        If (MyWorkBooks.Count = 0) Then
            iExcelApp.Quit()
        End If
    End Sub

End Class
