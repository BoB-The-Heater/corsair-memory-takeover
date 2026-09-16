using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;

namespace CorsairTakeover {
    public sealed class RamInfo { public string Part; public string Vendor; public long Bytes; }
    public sealed class AppInfo { public string Name; public string Version; }
    public sealed class DriverInfo { public string Name; public string Path; public int Start; public string State; }
    public sealed class NativeService {
        public string Name; public string DisplayName; public string Path; public string Command;
        public string State; public string StartMode; public string Account; public string Hash;
        public string Publisher; public string Role; public bool Trusted;
    }
    public sealed class ComponentCheck { public string Name; public bool Present; public string Detail; }
    public sealed class Snapshot {
        public string CapturedUtc; public string BoardVendor; public string BoardModel; public string Brand;
        public string BootUtc; public string OsVersion; public List<RamInfo> Ram = new List<RamInfo>();
        public List<AppInfo> Apps = new List<AppInfo>(); public List<DriverInfo> IcueDrivers = new List<DriverInfo>();
        public List<string> Errors = new List<string>(); public string GccPath; public string GccVersion;
        public string StorageVersion; public string IcueVersion; public string DcsVersion; public string DcsState;
        public int DcsStart; public int GccPid; public string GccStartUtc; public bool IcueRunning;
        public bool OtherCorsairUsb; public bool GccAutostart; public string Fingerprint;
        public bool RuleEligible; public string RuleReason; public string Health; public string HealthDetail;
        public LogEvidence Evidence = new LogEvidence(); public bool AssistantEnabled; public string AssistantState;
        public List<NativeService> NativeServices = new List<NativeService>();
        public List<ComponentCheck> Requirements = new List<ComponentCheck>();
        public List<string> ComponentFiles = new List<string>();
        public bool NativeConfirmed; public string NativeConfirmedUtc;
    }
    public sealed class LogEvidence {
        public bool CurrentSession; public int Modules; public int InstalledWriteFailures; public int PipeErrors;
        public bool Connected; public string SessionUtc; public string FileName; public string Note;
    }
    public sealed class RegistryValue {
        public string Key; public string Name; public bool Exists; public string Kind; public string Value;
    }
    public sealed class RegistryChange { public RegistryValue Before; public RegistryValue Applied; }
    public sealed class Enrollment {
        public int Schema = 1; public bool Enabled; public string OwnerSid; public string EnrolledUtc;
        public string Fingerprint; public string TaskName; public string InstalledExe; public string LastCheck;
        public string LastRepairUtc; public string RepairDate; public int RepairsToday;
        public List<RegistryChange> Changes = new List<RegistryChange>();
        public string Brand; public string NativeConfirmedUtc;
        public List<string> BaselineServices = new List<string>();
    }
    public sealed class OperationResult { public bool Success; public string Message; public string AtUtc; }
    public static class Rules {
        public static bool IsCorsair(RamInfo r) {
            return (r.Vendor ?? "").IndexOf("corsair", StringComparison.OrdinalIgnoreCase) >= 0 ||
                Regex.IsMatch((r.Part ?? "").Trim(), "^CM[THWPKDS][0-9]", RegexOptions.IgnoreCase);
        }
        public static string BrandOf(string vendor) {
            string v = (vendor ?? "").ToUpperInvariant();
            if (v.Contains("GIGABYTE")) return "GIGABYTE";
            if (v.Contains("ASUSTEK") || v.Contains("ASUS")) return "ASUS";
            if (v.Contains("MICRO-STAR") || v.Contains("MSI")) return "MSI";
            if (v.Contains("ASROCK")) return "ASRock";
            return "Other";
        }
        public static void Evaluate(Snapshot s) {
            if (NativePolicy.Supports(s.Brand)) { NativePolicy.Evaluate(s); return; }
            s.RuleEligible = false;
            s.RuleReason = "此品牌当前提供检测与官方接入指引，不执行自动恢复或启动配置修改。";

            if (s.Ram.Count == 0 || !s.Ram.Any(IsCorsair)) { s.Health = "未识别海盗船内存"; s.HealthDetail = "系统信息中没有匹配型号；不会尝试修复。"; }
            else if (s.Brand != "GIGABYTE") { s.Health = "需要按品牌检查接管条件"; s.HealthDetail = "检测到内存不等于官方软件已接管。请查看接入指引。"; }
            else if (s.GccPid == 0) { s.Health = "GCC 未运行"; s.HealthDetail = "没有活动的 GCC 会话；后台检查不会擅自重开已退出的软件。"; }
            else if (!s.Evidence.CurrentSession) { s.Health = "等待接管证据"; s.HealthDetail = "没有当前 GCC 进程启动后的海盗船连接日志。服务停止或手动启动本身不表示故障。"; }
            else if (s.Evidence.InstalledWriteFailures > 0 || s.Evidence.PipeErrors > 0) { s.Health = "发现当前连接异常"; s.HealthDetail = "当前 GCC 会话出现已安装内存写入失败或通信连接提前关闭。"; }
            else if (s.Evidence.Connected && s.Evidence.Modules == s.Ram.Count && s.DcsState == "Running") { s.Health = "接管链路正常"; s.HealthDetail = "当前日志显示内存已连接 GCC；请在官方软件中核实灯效。"; }
            else { s.Health = "接管状态未确认"; s.HealthDetail = "现有证据不足以判断已接管；不会盲目重启服务。"; }
        }
        public static bool CanAutoRepair(Snapshot s, Enrollment e, DateTime utc) {
            if (!NativePolicy.Supports(s.Brand) || e == null || !e.Enabled || !s.RuleEligible || e.Fingerprint != s.Fingerprint) return false;
            if (e.Brand != s.Brand || String.IsNullOrEmpty(e.NativeConfirmedUtc) || NativePolicy.ToStart(s,e).Count == 0) return false;
            DateTime last;
            if (DateTime.TryParse(e.LastRepairUtc, null, DateTimeStyles.RoundtripKind, out last) && utc - last.ToUniversalTime() < TimeSpan.FromMinutes(10)) return false;
            if (e.RepairDate == utc.ToString("yyyy-MM-dd") && e.RepairsToday >= 3) return false;
            return true;
        }
    }
    public static class LogParser {
        public static LogEvidence Parse(string text, int pid, DateTime bootUtc, DateTime processStartUtc) {
            LogEvidence e = new LogEvidence();
            if (pid <= 0) return e;
            string[] lines = (text ?? "").Split('\n'); int start = -1;
            for (int i=0; i<lines.Length; i++) {
                if (!lines[i].Contains("New client connected: \"GCC.exe\"") || !lines[i].Contains(":pid:" + pid + "\\")) continue;
                DateTime at;
                if (lines[i].Length < 19 || !DateTime.TryParseExact(lines[i].Substring(0,19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at)) continue;
                if (at < bootUtc.AddSeconds(-2) || at < processStartUtc.AddSeconds(-2)) continue;
                start = i; e.SessionUtc = at.ToString("o");
            }
            if (start < 0) return e;
            e.CurrentSession = true;
            HashSet<string> slots = new HashSet<string>();
            List<string> session = new List<string>();
            for (int i=start; i<lines.Length; i++) {
                // Another controller opening a new session invalidates attribution to this GCC session.
                if (i>start && lines[i].Contains("New client connected:")) { e.Note="出现其他客户端，归属不明确"; e.CurrentSession=false; return e; }
                session.Add(lines[i]);
                Match m=Regex.Match(lines[i], @"Detected module \[(\d+:\d+)\]:.*isCorsair=true; hasLightings=true");
                if(m.Success) slots.Add(m.Groups[1].Value);
                if(lines[i].Contains("Registered new client") && lines[i].Contains(":pid:"+pid+"\\")) e.Connected=true;
                if(lines[i].Contains("Unregistered client") || lines[i].Contains("Stopping service:")) e.Connected=false;
                if(lines[i].Contains("Error writing reply to client \"GCC.exe\"") && lines[i].Contains(":pid:"+pid+"\\")) e.PipeErrors++;
            }
            e.Modules=slots.Count;
            foreach(string line in session) {
                Match m=Regex.Match(line, @"Byte write failed: dram=\[(\d+:\d+)\]");
                if(m.Success && slots.Contains(m.Groups[1].Value)) e.InstalledWriteFailures++;
            }
            return e;
        }
    }
}
