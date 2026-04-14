Imports INFITF
Imports ProductStructureTypeLib
Imports MECMOD
Imports HybridShapeTypeLib
Imports Microsoft.VisualBasic.FileIO
Imports KnowledgewareTypeLib
Imports Rhino.Geometry
Imports Rhino.Render
Imports System.Security.Cryptography
Imports System.ComponentModel
Imports PARTITF
Imports Grasshopper.Kernel.Types
Imports Grasshopper.Kernel.Types.Transforms
Imports Rhino.Geometry.Collections
Imports CommonFunction.Transform
Imports Grasshopper.Kernel.Geometry


Public NotInheritable Class Common4Catia


    Public Shared CATIA As INFITF.Application
    'Public Shared CharDic As CharDictionary
    Shared Sub New()
        CATIA = ConnectCatia()
    End Sub

    ''' <summary>
    ''' 获取part根对象
    ''' </summary>
    ''' <param name="body"></param>
    ''' <returns></returns>
    Public Shared Function GetRootPart(body) As Part
        Do
            body = body.Parent
        Loop While InStr(body.Parent.Name, ".CATPart") = 0
        GetRootPart = body
    End Function

    Public Shared Function GetPartDoc(body) As PartDocument
        Dim i As Integer = 0
        Do
            body = body.Parent
            i = i + 1
        Loop While (InStr(body.Name, ".CATPart") = 0) And i < 10
        If i < 10 Then
            GetPartDoc = body
        Else
            GetPartDoc = Nothing
        End If

    End Function


    Public Shared Function GetProductDoc(body) As ProductDocument
        Dim i As Integer = 0
        Do
            body = body.Parent
            i = i + 1
        Loop While InStr(body.Name, ".CATProduct") = 0 And i < 10
        If i < 10 Then
            GetProductDoc = body
        Else
            GetProductDoc = Nothing
        End If
    End Function


    ''' <summary>
    ''' 判断subHybridbody是否是parentName的子级目录
    ''' </summary>
    ''' <param name="subHybridbody"></param>子级'subHybridbody
    ''' <param name="parentName"></param>父级名称
    ''' <returns></returns>
    Public Shared Function IsSubHybridBody(subHybridbody As HybridBody, parentName As String) As Boolean
        'If subHybridbody.Name = parentName Then
        '    Return True
        'End If

        Dim i As Integer = 0
        Do
            subHybridbody = subHybridbody.Parent
            i = i + 1
        Loop While subHybridbody.Name <> parentName And i < 10
        If i < 10 Then
            Return True
        Else
            Return False
        End If
    End Function








    ''' <summary>
    ''' 连接Catia
    ''' </summary>
    ''' <returns></returns>
    Public Shared Function ConnectCatia() As INFITF.Application
        Try
            CATIA = Interaction.GetObject(, "CATIA.Application")

            If CATIA IsNot Nothing Then
                ' ✅ 关键：关闭所有弹窗（防卡死）
                CATIA.DisplayFileAlerts = False
            End If

            Return CATIA
        Catch
            Return Nothing
        End Try
    End Function

    '============================
    ' 🔵 打开 STEP
    '============================
    Public Shared Function OpenSTEP(filePath As String) As PartDocument
        Try
            Dim doc As Document = CATIA.Documents.Open(filePath)

            If TypeOf doc Is PartDocument Then
                Return CType(doc, PartDocument)
            Else
                doc.Close()
                Return Nothing
            End If
        Catch
            Return Nothing
        End Try
    End Function

    '============================
    ' 🔵 另存为 STEP
    '============================
    Public Shared Function SaveAsSTEP(partDoc As PartDocument, targetPath As String) As Boolean
        Try
            partDoc.ExportData(targetPath, "stp")
            partDoc.Close()
            Return True
        Catch
            Try
                partDoc.Close()
            Catch
            End Try
            Return False
        End Try
    End Function


    '============================
    ' 🔵 单文件转换
    '============================
    Public Shared Function ConvertOneSTEP2(srcPath As String, dstPath As String) As Boolean
        Try
            If CATIA Is Nothing Then
                CATIA = ConnectCatia()
                If CATIA Is Nothing Then Return False
            End If

            '============================
            ' 🔵 打开 STEP
            '============================
            CATIA.Documents.Open(srcPath)

            Dim doc As INFITF.Document = CATIA.ActiveDocument
            If doc Is Nothing Then Return False

            '============================
            ' 🔥 关键：先另存为 CATPart
            '============================
            Dim tempPath As String = IO.Path.ChangeExtension(dstPath, ".CATPart")

            doc.SaveAs(tempPath)

            doc.Close()

            '============================
            ' 🔥 再重新打开 CATPart
            '============================
            Dim partDoc As MECMOD.PartDocument =
            CType(CATIA.Documents.Open(tempPath), MECMOD.PartDocument)

            partDoc.Part.Update()

            '============================
            ' 🔥 真正导出 STEP
            '============================
            partDoc.ExportData(dstPath, "stp")

            partDoc.Close()

            '============================
            ' 🔥 删除临时文件（可选）
            '============================
            If IO.File.Exists(tempPath) Then
                IO.File.Delete(tempPath)
            End If

            Return IO.File.Exists(dstPath)

        Catch ex As Exception
            Return False
        End Try
    End Function



    Public Shared Function ConvertOneSTEP3(srcPath As String, dstPath As String) As Boolean
        Dim stepDoc As INFITF.Document = Nothing
        Dim partDoc As MECMOD.PartDocument = Nothing

        Try
            If CATIA Is Nothing Then
                CATIA = ConnectCatia()
                If CATIA Is Nothing Then Return False
            End If

            '============================
            ' 🔵 1️⃣ 打开 STEP
            '============================
            stepDoc = CATIA.Documents.Open(srcPath)
            If stepDoc Is Nothing Then Return False

            '============================
            ' 🔥 2️⃣ 用 ExportData 转 CATPart（关键！）
            '============================
            Dim tempPath As String = IO.Path.ChangeExtension(dstPath, ".CATPart")

            stepDoc.ExportData(tempPath, "CATPart")

            ' ⚠️ 检查是否真的生成
            If Not IO.File.Exists(tempPath) Then
                stepDoc.Close()
                Return False
            End If

            stepDoc.Close()
            stepDoc = Nothing

            '============================
            ' 🔵 3️⃣ 打开 CATPart
            '============================
            partDoc = CType(CATIA.Documents.Open(tempPath), MECMOD.PartDocument)
            If partDoc Is Nothing Then Return False

            partDoc.Part.Update()

            '============================
            ' 🔵 4️⃣ 导出 STP
            '============================
            partDoc.ExportData(dstPath, "stp")

            partDoc.Close()
            partDoc = Nothing

            '============================
            ' 🔵 5️⃣ 删除临时文件
            '============================
            If IO.File.Exists(tempPath) Then
                IO.File.Delete(tempPath)
            End If

            Return IO.File.Exists(dstPath)

        Catch
            Return False

        Finally
            Try
                If stepDoc IsNot Nothing Then stepDoc.Close()
            Catch
            End Try

            Try
                If partDoc IsNot Nothing Then partDoc.Close()
            Catch
            End Try
        End Try
    End Function


    Public Shared Function ConvertOneSTEP_WithLog(srcPath As String, sourceRoot As String, targetRoot As String) As String

        Dim stepDoc As INFITF.Document = Nothing
        Dim partDoc As MECMOD.PartDocument = Nothing

        Try
            If CATIA Is Nothing Then
                CATIA = ConnectCatia()
                If CATIA Is Nothing Then Return "CATIA未连接"
            End If

            '============================
            ' 路径处理（保持子目录结构）
            '============================
            Dim relativePath = srcPath.Substring(sourceRoot.Length).TrimStart("\"c)
            Dim targetPath = IO.Path.Combine(targetRoot, relativePath)

            Dim dstDir = IO.Path.GetDirectoryName(targetPath)
            If Not IO.Directory.Exists(dstDir) Then
                IO.Directory.CreateDirectory(dstDir)
            End If

            '============================
            ' 1️⃣ 打开 STEP
            '============================
            stepDoc = CATIA.Documents.Open(srcPath)
            If stepDoc Is Nothing Then Return "打开失败"

            System.Threading.Thread.Sleep(200)

            '============================
            ' 2️⃣ 转 CATPart
            '============================
            Dim tempPath = IO.Path.ChangeExtension(targetPath, ".CATPart")

            stepDoc.ExportData(tempPath, "CATPart")

            If Not IO.File.Exists(tempPath) Then
                Return "STEP→CATPart失败"
            End If

            stepDoc.Close()
            stepDoc = Nothing

            '============================
            ' 3️⃣ 打开 CATPart
            '============================
            partDoc = CType(CATIA.Documents.Open(tempPath), MECMOD.PartDocument)
            If partDoc Is Nothing Then Return "CATPart打开失败"

            partDoc.Part.Update()

            '============================
            ' 4️⃣ 导出 STP
            '============================
            partDoc.ExportData(targetPath, "stp")

            If Not IO.File.Exists(targetPath) Then
                Return "CATPart→STP失败"
            End If

            partDoc.Close()
            partDoc = Nothing

            IO.File.Delete(tempPath)

            Return "OK"

        Catch ex As Exception
            Return ex.Message

        Finally
            Try
                If stepDoc IsNot Nothing Then stepDoc.Close()
            Catch
            End Try

            Try
                If partDoc IsNot Nothing Then partDoc.Close()
            Catch
            End Try
        End Try

    End Function



    Public Shared Sub BatchConvertSTEP(sourceDir As String, targetDir As String)

        Dim log As New System.Text.StringBuilder
        log.AppendLine("STEP转换日志")
        log.AppendLine("时间: " & Now.ToString())
        log.AppendLine("----------------------------------")

        Dim files = IO.Directory.GetFiles(sourceDir, "*.stp", IO.SearchOption.AllDirectories)

        For Each srcPath In files

            Dim result As String = ConvertOneSTEP_WithLog(srcPath, sourceDir, targetDir)

            If result <> "OK" Then
                log.AppendLine(srcPath & " 失败原因: " & result)
            End If

        Next

        '============================
        ' 🔥 写日志
        '============================
        Dim logPath = IO.Path.Combine(targetDir, "转换日志.txt")
        IO.File.WriteAllText(logPath, log.ToString())

    End Sub



    ''' <summary>
    ''' 将part文件另存为stp格式
    ''' </summary>
    ''' <param name="doc"></param>要另存的文档
    ''' <param name="fileName"></param>目标文件名
    Public Shared Sub SavePart2Stp(doc As INFITF.Document, fileName As String)
        doc.ExportData(fileName, "stp")
        'doc.Close()
    End Sub


    ''' <summary>
    ''' 将装配体的各个零件另存为stp格式
    ''' </summary>
    ''' <param name="product"></param>装配体根
    ''' <param name="path"></param>要保存的位置
    Public Shared Sub SaveAllParts2Stp(product As Product, path As String)
        Dim iSubProduct As Product
        For Each iSubProduct In product.Products
            Dim iProductDoc As Document = product.ReferenceProduct.Parent
            Dim iFullName As String = path + "\" + product.Name + "\" + iSubProduct.Name + ".stp"

            On Error Resume Next
            Dim iPartDoc As PartDocument = iSubProduct.ReferenceProduct.Parent
            On Error GoTo 0
            If Not (iPartDoc Is Nothing) Then
                Dim iPath = path + "\" + product.Name
                If Not FileIO.FileSystem.DirectoryExists(iPath) Then '判断目录是否存在
                    FileIO.FileSystem.CreateDirectory(iPath) '不存在就建立目录
                End If
                iPartDoc.ExportData(iFullName, "stp") '保存零件
            End If


            If iSubProduct.Products.Count > 0 Then '判断是否存在下一级
                SaveAllParts2Stp(iSubProduct, path)
            End If

        Next
    End Sub





    Public Shared Function CreatProductDocument(Name As String, ByRef productDoc As ProductDocument) As Boolean
        Dim needNewFile As Boolean
        Err.Clear()
        CreatProductDocument = True
        productDoc = CATIA.Documents.Add("Product") '新建产品文件
        On Error Resume Next
        productDoc.Product.PartNumber = Name '产品编号就是源文件的零件编号
        If Err.Number <> 0 Then
            CreatProductDocument = False
            MsgBox("已经存在同名的Product文件，请先关闭！")
            productDoc.Close()
        End If
        'On Error GoTo 0
    End Function


    ''' <summary>
    ''' 在product1下建立product2，名字为categoryName
    ''' </summary>
    ''' <param name="parentProduct"></param>
    ''' <param name="categoryName"></param>
    ''' <returns></returns>
    Public Shared Function AddSonProduct(parentProduct As Product, categoryName As String) As Product
        Dim sonProduct As Product
        sonProduct = parentProduct.Products.AddNewComponent("Product", categoryName)
        Return sonProduct
    End Function



















    ''' <summary>
    ''' 将指定的part插入到position对象下面
    ''' </summary>
    ''' <param name="position"></param>要插入的位置
    ''' <param name="fullFileName"></param>part对应的文件名
    ''' <param name="instanceName"></param>实例名称
    ''' <param name="partNumber"></param>零件编号
    ''' <returns></returns>
    Public Shared Function InsertThePartToProduct(position As Product, fullFileName As String, instanceName As String, partNumber As String) As Product
        Dim fullFileName2() As Object = {fullFileName}
        position.Products.AddComponentsFromFiles(fullFileName2, "All") '用这个方法添加进来的装配可以删除，也可以编辑
        'rootProduct.AddMasterShapeRepresentation(fullFileName) '用这个方法添加进来的装配删除不了，也无法编辑
        Dim count As Integer = position.Products.Count
        Dim currentProduct As Product = position.Products.Item(count)
        If partNumber <> String.Empty Then
            currentProduct.PartNumber = partNumber  '零件编号
        End If
        If instanceName <> String.Empty Then
            currentProduct.Name = instanceName  '实例名称
        End If

        'Dim currentPart As Part = currentProduct.ReferenceProduct.Parent.part
        Return currentProduct

    End Function











    Function GetPath(body As HybridBody, rootName As String) As String '包含根对象
        Dim retVal As String
        If body.Name <> rootName Then
            'retVal = GetPath(body.OrderedGeometricalSets.Parent, RootName) & "\" & body.name
            retVal = GetPath(body.HybridBodies.Parent, rootName) & "\" & body.Name
        Else
            retVal = body.Name
        End If

        Return retVal
        'GetPath = retVal
    End Function


    Shared Function OpenTheFile(fileName As String) As INFITF.Documents
        Dim doc As INFITF.Documents = CATIA.Documents.Open(fileName)
        Return doc
    End Function


    Public mSketch As Sketch
    Public mFactory As Factory2D
    Public mPart As Part

    Sub OpenEdit(sketchName As String)
        Dim oDoc As Document = CATIA.ActiveDocument
        mPart = oDoc.Part
        Dim oBodies As Bodies = mPart.Bodies
        Dim oBody As Body = oBodies.Item(1)
        Dim oPlane As Reference = mPart.OriginElements.PlaneXY
        mSketch = oBody.Sketches.Add(oPlane)
        mSketch.Name = sketchName
        mFactory = mSketch.OpenEdition()
    End Sub

    Sub CloseEdit()
        mSketch.CloseEdition()
        mPart.InWorkObject = mSketch
        mPart.Update()
    End Sub



    Sub AddLine(curve As LineCurve)
        Dim pts As MECMOD.Point2D
        pts = mFactory.CreatePoint(curve.PointAtStart.X, curve.PointAtStart.Y)

        Dim pte As MECMOD.Point2D
        pte = mFactory.CreatePoint(curve.PointAtEnd.X, curve.PointAtEnd.Y)

        Dim line As Line2D = mFactory.CreateLine(curve.PointAtStart.X, curve.PointAtStart.Y, curve.PointAtEnd.X, curve.PointAtEnd.Y)
        line.StartPoint = pts '将点附着在直线上
        line.EndPoint = pte '将点附着在直线上
    End Sub

    Sub AddArc(curve As ArcCurve)

        Dim theArc As Arc = curve.Arc
        Dim ptc As MECMOD.Point2D
        ptc = mFactory.CreatePoint(theArc.Center.X, theArc.Center.Y)

        Dim pts As MECMOD.Point2D
        pts = mFactory.CreatePoint(theArc.StartPoint.X, theArc.StartPoint.Y)

        Dim pte As MECMOD.Point2D
        pte = mFactory.CreatePoint(theArc.EndPoint.X, theArc.EndPoint.Y)


        Dim start_angle As Double = theArc.StartAngle
        Dim end_angle As Double = theArc.EndAngle

        Dim cir2D As Circle2D = mFactory.CreateCircle(theArc.Center.X, theArc.Center.Y, theArc.Radius, start_angle, end_angle)
        cir2D.StartPoint = pts
        cir2D.EndPoint = pte
        cir2D.CenterPoint = ptc
    End Sub









    Sub AddCircle(curve As ArcCurve)

        Dim theArc As Arc = curve.Arc
        Dim cir2d As Circle2D = mFactory.CreateClosedCircle(theArc.Center.X, theArc.Center.Y, theArc.Radius)

        Dim pts As MECMOD.Point2D = mFactory.CreatePoint(curve.PointAtStart.X, curve.PointAtStart.Y)
        Dim pte As MECMOD.Point2D = mFactory.CreatePoint(curve.PointAtEnd.X, curve.PointAtEnd.Y)
        Dim ptc As MECMOD.Point2D = mFactory.CreatePoint(theArc.Center.X, theArc.Center.Y)

        cir2d.StartPoint = pts
        cir2d.EndPoint = pte
        cir2d.CenterPoint = ptc
    End Sub


    Sub AddPoint(point As Point3d)
        Dim pt As MECMOD.Point2D
        pt = mFactory.CreatePoint(point.X, point.Y)
    End Sub

    Sub AddPolyline(curve As PolylineCurve)
        Dim count_point As Integer = curve.PointCount
        Dim count_seg As Integer = curve.ToPolyline().SegmentCount
        Dim pline As Polyline = Nothing
        curve.TryGetPolyline(pline) '获取顶点
        Dim i As Integer
        Dim pt() As MECMOD.Point2D
        ReDim pt(count_point - 1)
        For i = 0 To count_point - 1
            pt(i) = mFactory.CreatePoint(pline(i).X, pline(i).Y)
        Next i

        For i = 0 To count_point - 2
            Dim oPoint0 As Double（） = {pline(i).X, pline(i).Y}
            Dim oPoint1 As Double（） = {pline(i + 1).X, pline(i + 1).Y}
            Dim line As Line2D = mFactory.CreateLine(oPoint0(0), oPoint0(1), oPoint1(0), oPoint1(1))
            line.StartPoint = pt(i) '将点附着在直线上
            line.EndPoint = pt(i + 1) '将点附着在直线上
        Next
    End Sub

    Sub AddEllipse(curve As Rhino.Geometry.Ellipse, start_point As Point3d, end_point As Point3d, start_angle As Double, end_angle As Double)
        Dim ptc As MECMOD.Point2D = mFactory.CreatePoint(curve.Center.X, curve.Center.Y)
        Dim F1, F2 As Point3d
        curve.GetFoci(F1, F2)
        Dim iMajorX As Double = F1.X - F2.X
        Dim iMajorY As Double = F1.Y - F2.Y
        Dim crv As Ellipse2D = mFactory.CreateEllipse(curve.Center.X, curve.Center.Y, iMajorX, iMajorY, curve.Radius1, curve.Radius2, start_angle, end_angle)

        Dim pts As MECMOD.Point2D = mFactory.CreatePoint(start_point.X, start_point.Y)
        Dim pte As MECMOD.Point2D = mFactory.CreatePoint(end_point.X, end_point.Y)

        crv.CenterPoint = ptc
        crv.StartPoint = pts
        crv.EndPoint = pte

    End Sub

    ''' <summary>
    ''' 此函数有问题，不能完全布置Rhino中的曲线
    ''' </summary>
    ''' <param name="curve"></param>
    Sub AddNurbsCurve(curve As NurbsCurve, hasTangent As Boolean)
        Dim pt_list As NurbsCurvePointList = curve.Points
        'Dim knot_list As NurbsCurveKnotList = curve.Knots
        Dim count As Integer = pt_list.Count
        Dim pt_con_arr As ControlPoint2D()
        ReDim pt_con_arr(count - 1)
        Dim arr
        ReDim arr(count - 1)
        For i = 0 To count - 1
            Dim t As Double
            Dim pt_tem As Point3d = New Point3d(pt_list(i).X, pt_list(i).Y, pt_list(i).Z)
            curve.ClosestPoint(pt_tem, t)

            Dim pt_tem2 As Point3d = curve.PointAt(t)
            pt_con_arr(i) = mFactory.CreateControlPoint(pt_tem2.X, pt_tem2.Y)
            If hasTangent Then
                Dim v As Vector3d = curve.TangentAt(t)
                pt_con_arr(i).SetTangent(v.X, v.Y)
            End If
            arr(i) = pt_con_arr(i) '转换为object类型，在catia中是变体类型
        Next

        mFactory.CreateSpline(arr) '该函数接受变体数组类型
    End Sub

    Sub AddNurbsCurve2(controlPoint As Point3d(), tangent As Vector3d())
        Dim count As Integer = controlPoint.Length
        Dim pt_con_arr As ControlPoint2D()
        ReDim pt_con_arr(count - 1)
        Dim arr
        ReDim arr(count - 1)
        For i = 0 To count - 1
            pt_con_arr(i) = mFactory.CreateControlPoint(controlPoint(i).X, controlPoint(i).Y)
            pt_con_arr(i).SetTangent(tangent（i）.X, tangent（i）.Y)
            arr(i) = pt_con_arr(i) '转换为object类型，在catia中是变体类型
        Next
        mFactory.CreateSpline(arr) '该函数接受变体数组类型
    End Sub

    Sub AddNurbsCurve2(controlPoint As Point3d())
        Dim count As Integer = controlPoint.Length
        Dim pt_con_arr As ControlPoint2D()
        ReDim pt_con_arr(count - 1)
        Dim arr
        ReDim arr(count - 1)
        For i = 0 To count - 1
            pt_con_arr(i) = mFactory.CreateControlPoint(controlPoint(i).X, controlPoint(i).Y)
            arr(i) = pt_con_arr(i) '转换为object类型，在catia中是变体类型
        Next
        mFactory.CreateSpline(arr) '该函数接受变体数组类型
    End Sub


    Protected Overrides Sub Finalize()
        MyBase.Finalize()
    End Sub
End Class
