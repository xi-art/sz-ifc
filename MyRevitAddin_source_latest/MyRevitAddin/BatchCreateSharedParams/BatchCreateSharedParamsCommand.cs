using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace MyRevitAddin.BatchCreateSharedParams
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class BatchCreateSharedParamsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                MiniLog.Info("BatchCreateSharedParams:Execute:ENTER");
                try { EnsureWinFormsReady(); }
                catch (Exception wfEx) { MiniLog.Error("EnsureWinFormsReady", wfEx); }

                UIApplication uiApp = commandData?.Application;
                UIDocument uiDoc = null;
                Document doc = null;
                try { uiDoc = uiApp?.ActiveUIDocument; }
                catch (Exception ex) { MiniLog.Error("Read-UIDoc", ex); }
                try
                {
                    doc = uiDoc?.Document ?? uiApp?.Application?.Documents?.Cast<Document>().FirstOrDefault(d => !d.IsFamilyDocument && d.IsValidObject);
                }
                catch (Exception ex) { MiniLog.Error("Read-Doc", ex); }

                if (doc == null)
                {
                    message = "当前没有可操作的项目文档。请打开一个 .rvt 项目后再试。";
                    try { TaskDialog.Show("批量建共享参数", message); }
                    catch { MessageBox.Show(message, "批量建共享参数", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    return Result.Failed;
                }

                using (var dlg = new BatchCreateSharedParamsDialog(uiApp, doc))
                {
                    MiniLog.Info("BatchCreateSharedParams:ShowDialog:INVOKE");
                    System.Windows.Forms.DialogResult r = dlg.ShowDialog();
                    MiniLog.Info("BatchCreateSharedParams:ShowDialog:RETURN=" + r);
                    return r == System.Windows.Forms.DialogResult.OK ? Result.Succeeded : Result.Cancelled;
                }
            }
            catch (Exception exOuter)
            {
                try { MiniLog.Error("BatchCreateSharedParams:Execute-OUTER-FATAL", exOuter); }
                catch { }
                string msg = "批量建共享参数严重错误：" + exOuter.GetType().Name + ": " + exOuter.Message;
                try { TaskDialog.Show("批量建共享参数 - 严重错误", msg + "\n\n" + exOuter.StackTrace); }
                catch { try { MessageBox.Show(msg, "批量建共享参数", MessageBoxButtons.OK, MessageBoxIcon.Stop); } catch { } }
                message = msg;
                return Result.Failed;
            }
        }

        private static void EnsureWinFormsReady()
        {
            try
            {
                if (System.Windows.Forms.Application.VisualStyleState == System.Windows.Forms.VisualStyles.VisualStyleState.NoneEnabled)
                    System.Windows.Forms.Application.EnableVisualStyles();
            }
            catch { }
        }
    }

    internal class SharedParamDef
    {
        public string Name { get; set; }
        public string GroupName { get; set; }
        public ParameterType ParamType { get; set; }
        public bool Instance { get; set; }
        public string Description { get; set; }
        public bool UserModifiable { get; set; }
        public BuiltInParameterGroup ParamGroup { get; set; }
    }

    internal static class MiniLog
    {
        private static string _lastFile;
        private static readonly object _lock = new object();
        public static string LastLogFileHint { get { return _lastFile == null ? "" : "最近一次诊断日志：" + _lastFile; } }

        public static void Info(string msg)
        {
            try
            {
                lock (_lock)
                {
                    string dir = Path.Combine(Path.GetTempPath(), "MyRevitAddin_BatchCreateSP_Logs");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "batchcreatesp_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    _lastFile = file;
                    File.AppendAllText(file, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] INFO  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Error(string tag, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    string dir = Path.Combine(Path.GetTempPath(), "MyRevitAddin_BatchCreateSP_Logs");
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "batchcreatesp_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    _lastFile = file;
                    string text = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] ERROR [" + tag + "] " + (ex == null ? "" : ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace);
                    File.AppendAllText(file, text + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
