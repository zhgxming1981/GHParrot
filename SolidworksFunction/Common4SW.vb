Imports System.Runtime.InteropServices
Imports SldWorks
Imports SolidWorks
Imports SwConst
'Imports System.Windows.Forms
Public Class Common4SW
    Public Shared swApp As SldWorks.SldWorks = Nothing
    Public Shared swModel As ModelDoc2 = Nothing

    'Shared Sub New()
    '    swApp = ConnectSolidworks()
    'End Sub

    ''' <summary>
    ''' 连接Solidworks
    ''' </summary>
    ''' <returns></returns>
    Public Shared Function ConnectSolidworks() As Boolean
        If swApp Is Nothing Then
            On Error Resume Next
            swApp = Interaction.GetObject(, "SldWorks.Application")
            If swApp Is Nothing Then
                swApp = New SldWorks.SldWorks()
            End If
        End If
        If swApp Is Nothing Then
            MsgBox("不能连接上Solidworks!")
            Return False
        Else
            swApp.Visible = True
            Return True
        End If
    End Function


    Public Shared Function OpenTheFile(filePath As String) As Boolean
        Try
            ' 2. 判断文件是否存在
            If Not IO.File.Exists(filePath) Then
                Throw New Exception("文件不存在：" & filePath)
            End If

            ' 3. 打开文件
            Dim errors As Integer = 0
            Dim warnings As Integer = 0

            swModel = swApp.OpenDoc6(
            filePath,
            swDocumentTypes_e.swDocNONE,
            swOpenDocOptions_e.swOpenDocOptions_ReadOnly,
            "",
            errors,
            warnings
            )

            If swModel Is Nothing Or errors <> 0 Then
                Throw New Exception(errors)
            End If

            Return True

        Catch ex As Exception
            Select Case ex.Message
                Case "1024"
                    MsgBox("错误码 1024：文件未找到！")
                Case "2097152"
                    MsgBox("错误码 2097152：文件需要修复！")
                Case Else
                    MsgBox("错误码 " & ex.Message)
            End Select
            Return False
        End Try
    End Function


End Class
