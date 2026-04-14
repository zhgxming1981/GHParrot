using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using Excel = Microsoft.Office.Interop.Excel;

namespace NS_Parrot
{
    public class GH2ExcelTemplate : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the GH2ExcelTemplate class.
        /// </summary>
        public GH2ExcelTemplate()
          : base("GH2ExcelTemplate", "GH2Excel模板",
              "套用模板填写Excel表格",
              "Parrot", "ExcelCAD")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("TemplatePath", "TP", "模板路径", GH_ParamAccess.item);
            pManager.AddTextParameter("TemplateSheet", "TS", "模板Sheet", GH_ParamAccess.item);
            pManager.AddTextParameter("DataPath", "DP", "数据文件路径", GH_ParamAccess.item);
            pManager.AddTextParameter("DataSheet", "DS", "数据Sheet", GH_ParamAccess.item);
            pManager.AddTextParameter("StartCell", "SC", "起始单元格", GH_ParamAccess.item);
            pManager.AddTextParameter("DataList", "DL", "数据列表 A|B|C", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Write", "W", "执行写入", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Log", "L", "日志", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string templatePath = "";
            string templateSheet = "";
            string dataPath = "";
            string dataSheet = "";
            string startCell = "";
            List<string> dataList = new List<string>();
            bool write = false;

            if (!DA.GetData(0, ref templatePath)) return;
            if (!DA.GetData(1, ref templateSheet)) return;
            if (!DA.GetData(2, ref dataPath)) return;
            if (!DA.GetData(3, ref dataSheet)) return;
            if (!DA.GetData(4, ref startCell)) return;
            if (!DA.GetDataList(5, dataList)) return;
            if (!DA.GetData(6, ref write)) return;

            if (!write)
            {
                DA.SetData(0, "Write = false");
                return;
            }

            if (!File.Exists(templatePath))
            {
                DA.SetData(0, "模板不存在");
                return;
            }

            Excel.Application app = new Excel.Application();
            app.DisplayAlerts = false;

            try
            {
                // ===== 打开模板 =====
                Excel.Workbook templateWb = app.Workbooks.Open(templatePath);
                Excel.Worksheet templateWs = null;

                foreach (Excel.Worksheet s in templateWb.Worksheets)
                {
                    if (s.Name == templateSheet)
                    {
                        templateWs = s;
                        break;
                    }
                }

                if (templateWs == null)
                {
                    templateWb.Close(false);
                    app.Quit();
                    DA.SetData(0, "模板Sheet不存在");
                    return;
                }

                // ===== 打开 / 创建目标文件 =====
                Excel.Workbook targetWb;

                if (File.Exists(dataPath))
                {
                    targetWb = app.Workbooks.Open(dataPath);
                }
                else
                {
                    targetWb = app.Workbooks.Add();
                    targetWb.SaveAs(dataPath);
                }

                // ===== 如果已存在同名Sheet → 删除 =====
                foreach (Excel.Worksheet s in targetWb.Worksheets)
                {
                    if (s.Name == dataSheet)
                    {
                        s.Delete();
                        break;
                    }
                }

                // ===== 复制模板Sheet =====
                templateWs.Copy(After: targetWb.Worksheets[targetWb.Worksheets.Count]);

                Excel.Worksheet newSheet = targetWb.Worksheets[targetWb.Worksheets.Count];
                newSheet.Name = dataSheet;

                // ===== 写入数据 =====
                ParseCell(startCell, out int startRow, out int startCol);

                int currentRow = startRow;

                foreach (var line in dataList)
                {
                    var values = line.Split('|');

                    for (int i = 0; i < values.Length; i++)
                    {
                        newSheet.Cells[currentRow, startCol + i] = values[i];
                    }

                    currentRow++;
                }

                // ===== 保存 =====
                targetWb.Save();

                // ===== 关闭 =====
                templateWb.Close(false);
                targetWb.Close();
                app.Quit();

                DA.SetData(0, "完成 ✅（模板已复制 + 写入数据）");
            }
            catch (Exception ex)
            {
                app.Quit();
                DA.SetData(0, ex.Message);
            }
        }


        private void ParseCell(string cell, out int row, out int col)
        {
            int i = 0;
            col = 0;

            while (i < cell.Length && Char.IsLetter(cell[i]))
            {
                col = col * 26 + (Char.ToUpper(cell[i]) - 'A' + 1);
                i++;
            }

            row = int.Parse(cell.Substring(i));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("BA55051E-8A2B-40D5-A9AF-E1B48BAE83FF"); }
        }
    }
}