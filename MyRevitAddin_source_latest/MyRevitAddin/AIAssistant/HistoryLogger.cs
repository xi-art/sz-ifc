using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MyRevitAddin.AIAssistant
{
    public static class HistoryLogger
    {
        private static readonly object _lock = new object();
        private static string _rootDir;

        public static string RootDir
        {
            get
            {
                if (_rootDir == null)
                {
                    _rootDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    try
                    {
                        if (Directory.Exists("F:\\vs\\code\\MyRevitAddin\\data"))
                            _rootDir = "F:\\vs\\code\\MyRevitAddin\\data";
                    }
                    catch { }
                }
                return _rootDir;
            }
            set { _rootDir = value; }
        }

        private static string EnsureDir(string category)
        {
            string dir = Path.Combine(RootDir, category, DateTime.Now.ToString("yyyy-MM"));
            lock (_lock)
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            return dir;
        }

        private static string Timestamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        private static string DateTag()
        {
            return DateTime.Now.ToString("yyyy-MM-dd");
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                    sb.Append(' ');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static void Chat(string role, string content, string extraInfo = null)
        {
            try
            {
                string dir = EnsureDir("chat");
                string file = Path.Combine(dir, "chat_" + DateTag() + ".log");
                string line =
                    "\n═══════════════════════════════════════════\n" +
                    "[" + Timestamp() + "] [" + role.ToUpperInvariant() + "] " + (extraInfo ?? "") + "\n" +
                    Sanitize(content ?? "(空)") + "\n";
                lock (_lock) File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("HistoryLogger.Chat 失败: " + ex.Message); }
        }

        public static void Tool(string toolName, string argsJson, string resultJson, string source = "auto")
        {
            try
            {
                string dir = EnsureDir("tools");
                string file = Path.Combine(dir, "tools_" + DateTag() + ".log");
                string sessionId = DateTime.Now.Ticks.ToString("X") + Guid.NewGuid().ToString("N").Substring(0, 4);
                string line =
                    "\n─────────────────────────────────────────\n" +
                    "[" + Timestamp() + "] [TOOL:" + source + "] session=" + sessionId + "\n" +
                    "  工具名: " + toolName + "\n" +
                    "  参数  : " + Sanitize(argsJson ?? "(空)") + "\n" +
                    "  返回  : (" + (resultJson ?? "").Length + " 字符)\n" +
                    "─────────── 原始 JSON 返回 ───────────\n" +
                    Sanitize(resultJson ?? "(空)") + "\n" +
                    "──────────── 返回结束 ────────────\n";
                lock (_lock) File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("HistoryLogger.Tool 失败: " + ex.Message); }
        }

        public static void Error(string location, Exception ex, string context = null)
        {
            try
            {
                string dir = EnsureDir("errors");
                string file = Path.Combine(dir, "errors_" + DateTag() + ".log");
                string errMsg =
                    "\n███████████████████████████████████████████████\n" +
                    "[" + Timestamp() + "] [ERROR @ " + (location ?? "?") + "]\n" +
                    "  上下文: " + Sanitize(context ?? "(无)") + "\n" +
                    "  类型  : " + (ex == null ? "(null exception)" : ex.GetType().FullName) + "\n" +
                    "  消息  : " + (ex == null ? "(null)" : Sanitize(ex.Message)) + "\n" +
                    "────────────── StackTrace ──────────────\n" +
                    (ex == null ? "(无)" : Sanitize(ex.StackTrace ?? "(无堆栈)")) + "\n" +
                    "────────── InnerException ──────────\n" +
                    (ex == null || ex.InnerException == null ? "(无内部异常)" :
                        "  Type: " + ex.InnerException.GetType().FullName + "\n" +
                        "  Msg : " + Sanitize(ex.InnerException.Message) + "\n" +
                        "  Stack: " + Sanitize(ex.InnerException.StackTrace ?? "(无)")) + "\n" +
                    "███████████████████████████████████████████████\n";
                lock (_lock) File.AppendAllText(file, errMsg, Encoding.UTF8);
            }
            catch { }
        }

        public static void Operation(string opName, Dictionary<string, object> args, string result, string extraInfo = null)
        {
            try
            {
                string dir = EnsureDir("operations");
                string file = Path.Combine(dir, "operations_" + DateTag() + ".log");
                string argsStr;
                if (args == null) argsStr = "(空)";
                else
                {
                    StringBuilder sb = new StringBuilder();
                    foreach (var kv in args)
                    {
                        sb.Append("\n      - ").Append(kv.Key).Append(" = ");
                        string v = kv.Value == null ? "(null)" : kv.Value.ToString();
                        if (v.Length > 300) v = v.Substring(0, 300) + " ...(长度=" + v.Length + ")";
                        sb.Append(Sanitize(v));
                    }
                    argsStr = sb.ToString();
                }
                string line =
                    "\n┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈\n" +
                    "[" + Timestamp() + "] [OP: " + opName + "] " + (extraInfo ?? "") + "\n" +
                    "  参数列表:" + argsStr + "\n" +
                    "  执行结果 (" + (result ?? "").Length + " 字符):\n" +
                    Sanitize(result ?? "(空)") + "\n" +
                    "┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈\n";
                lock (_lock) File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine("HistoryLogger.Operation 失败: " + ex.Message); }
        }

        public static void Raw(string category, string fileNameSuffix, string content)
        {
            try
            {
                string dir = EnsureDir(category);
                string safeName = string.IsNullOrEmpty(fileNameSuffix) ? "raw" : fileNameSuffix;
                string file = Path.Combine(dir, DateTag() + "_" + safeName + ".txt");
                lock (_lock) File.AppendAllText(file,
                    "\n[" + Timestamp() + "]\n" + content + "\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
