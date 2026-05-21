using CommonFunction;
using Grasshopper.Kernel;
using Rhino;
using Rhino.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace NS_Parrot
{
    public class RecoverRhinoUI : GH_Component
    {
        private bool _lastRun;

        public RecoverRhinoUI()
          : base("RecoverRhinoUI", "FixRhUI",
              "Try to recover a frozen Rhino sidebar or panel UI without restarting Rhino",
              "Parrot", "Rhino")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Run", "R", "Run once when toggled from False to True", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("ReopenPanels", "RP", "Close and reopen currently open Rhino panels", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBooleanParameter("Success", "S", "Whether the recovery action completed", GH_ParamAccess.item);
            pManager.AddTextParameter("Message", "M", "Recovery result", GH_ParamAccess.item);
            pManager.AddIntegerParameter("PanelCount", "P", "Number of open panels processed", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            if (!CHardware.CheckLegality())
                return;

            bool run = false;
            bool reopenPanels = true;
            DA.GetData(0, ref run);
            DA.GetData(1, ref reopenPanels);

            bool shouldRun = run && !_lastRun;
            _lastRun = run;

            if (!shouldRun)
            {
                DA.SetData(0, false);
                DA.SetData(1, "Waiting for trigger. Toggle Run from False to True to execute recovery once.");
                DA.SetData(2, 0);
                return;
            }

            bool success = false;
            string message = string.Empty;
            int panelCount = 0;

            try
            {
                RunRecoveryOnUiThread(reopenPanels, out success, out message, out panelCount);
            }
            catch (Exception ex)
            {
                success = false;
                message = "Failed to recover Rhino UI: " + ex.Message;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, message);
            }

            if (!success)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message);
            else
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, message);

            DA.SetData(0, success);
            DA.SetData(1, message);
            DA.SetData(2, panelCount);
        }

        private static void RunRecoveryOnUiThread(bool reopenPanels, out bool success, out string message, out int panelCount)
        {
            success = false;
            message = "Rhino UI recovery did not run.";
            panelCount = 0;

            Exception capturedException = null;
            bool localSuccess = false;
            string localMessage = string.Empty;
            int localPanelCount = 0;
            using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                RhinoApp.InvokeOnUiThread((Action)(() =>
                {
                    try
                    {
                        var panelIds = new List<Guid>();
                        var openPanelIds = Panels.GetOpenPanelIds();
                        if (openPanelIds != null)
                            panelIds = openPanelIds.Distinct().ToList();

                        localPanelCount = panelIds.Count;

                        PumpRhinoUi();

                        var doc = RhinoDoc.ActiveDoc;
                        if (doc != null)
                            doc.Views.Redraw();

                        if (reopenPanels && panelIds.Count > 0)
                        {
                            foreach (Guid panelId in panelIds)
                            {
                                try
                                {
                                    Panels.ClosePanel(panelId);
                                }
                                catch
                                {
                                }
                            }

                            PumpRhinoUi();

                            foreach (Guid panelId in panelIds)
                            {
                                try
                                {
                                    Panels.OpenPanel(panelId);
                                }
                                catch
                                {
                                }
                            }
                        }

                        PumpRhinoUi();

                        if (doc != null)
                            doc.Views.Redraw();

                        localSuccess = true;
                        localMessage = reopenPanels
                            ? $"Rhino UI recovery ran and reopened {localPanelCount} open panel tabs."
                            : "Rhino UI recovery ran with message pumping and view redraw.";
                    }
                    catch (Exception ex)
                    {
                        capturedException = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                }));

                if (!done.Wait(TimeSpan.FromSeconds(8)))
                    throw new TimeoutException("Rhino UI thread did not finish the recovery action within 8 seconds.");
            }

            if (capturedException != null)
                throw capturedException;

            success = localSuccess;
            message = localMessage;
            panelCount = localPanelCount;
        }

        private static void PumpRhinoUi()
        {
            RhinoApp.Wait();
            Application.DoEvents();
            RhinoApp.Wait();
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return GeneratedIcon.Get("gen_RecoverRhinoUI"); }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("52C04744-CC5F-4AFA-92AF-0A89B190BD0E"); }
        }
    }
}
