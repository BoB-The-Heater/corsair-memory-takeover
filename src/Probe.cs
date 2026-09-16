using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Management;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CorsairTakeover {
    public static class Probe {
        public static readonly string GccDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GIGABYTE", "Control Center");
        public static readonly string DcsDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Corsair", "Corsair Device Control Service", "bin");
        public static readonly string IcueDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Corsair", "Corsair iCUE5 Software");
        public static Snapshot Scan() {
            Snapshot s=new Snapshot(); s.CapturedUtc=DateTime.UtcNow.ToString("o"); s.GccPath=Path.Combine(GccDir,"GCC.exe");
            try {
                using(var search=new ManagementObjectSearcher("SELECT Manufacturer,Product FROM Win32_BaseBoard"))
                foreach(ManagementObject o in search.Get()) { s.BoardVendor=Text(o["Manufacturer"]); s.BoardModel=Text(o["Product"]); break; }
                using(var search=new ManagementObjectSearcher("SELECT Version,LastBootUpTime FROM Win32_OperatingSystem"))
                foreach(ManagementObject o in search.Get()) { s.OsVersion=Text(o["Version"]); s.BootUtc=ManagementDateTimeConverter.ToDateTime(Text(o["LastBootUpTime"])).ToUniversalTime().ToString("o"); break; }
                using(var search=new ManagementObjectSearcher("SELECT Manufacturer,PartNumber,Capacity FROM Win32_PhysicalMemory"))
                foreach(ManagementObject o in search.Get()) s.Ram.Add(new RamInfo { Vendor=Text(o["Manufacturer"]),Part=Text(o["PartNumber"]),Bytes=Convert.ToInt64(o["Capacity"]) });
            } catch(Exception ex) { s.Errors.Add("硬件信息读取失败："+ex.Message); }
            s.Brand=Rules.BrandOf(s.BoardVendor);
            try { ReadApps(s); } catch(Exception ex) { s.Errors.Add("软件版本读取失败："+ex.Message); }
            s.GccVersion=Version(s,"GIGABYTE Control Center",true); s.StorageVersion=Version(s,"GIGABYTE Storage Library",false);
            s.IcueVersion=Version(s,"Corsair iCUE5 Software",false);
            if(String.IsNullOrEmpty(s.IcueVersion)) s.IcueVersion=s.Apps.Where(a=>Regex.IsMatch(a.Name,@"(?:Corsair\s+)?iCUE",RegexOptions.IgnoreCase)).Select(a=>a.Version).FirstOrDefault();
            string dcs=Path.Combine(DcsDir,"CorsairDeviceControlService.exe");
            if(File.Exists(dcs)) s.DcsVersion=FileVersionInfo.GetVersionInfo(dcs).FileVersion;
            try {
                using(var search=new ManagementObjectSearcher("SELECT Name,State,PathName FROM Win32_Service WHERE Name='CorsairDeviceControlService'"))
                foreach(ManagementObject o in search.Get()) { s.DcsState=Text(o["State"]); using(var k=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\CorsairDeviceControlService")) s.DcsStart=k==null ? -1 : Convert.ToInt32(k.GetValue("Start",-1)); }
                using(var search=new ManagementObjectSearcher("SELECT Name,State,PathName FROM Win32_SystemDriver WHERE Name LIKE 'CorsairLLAccess%'"))
                foreach(ManagementObject o in search.Get()) {
                    string path=NormalizeDriverPath(Text(o["PathName"]));
                    if(!String.Equals(path,Path.Combine(IcueDir,"CorsairLLAccess64.sys"),StringComparison.OrdinalIgnoreCase)) continue;
                    string name=Text(o["Name"]); if(!Regex.IsMatch(name,"^CorsairLLAccess[0-9A-Fa-f]{40}$")) continue;
                    using(var k=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\"+name))
                    s.IcueDrivers.Add(new DriverInfo{Name=name,Path=path,State=Text(o["State"]),Start=k==null ? -1 : Convert.ToInt32(k.GetValue("Start",-1))});
                }
                using(var search=new ManagementObjectSearcher("SELECT Name,ProcessId,CreationDate FROM Win32_Process WHERE Name='GCC.exe' OR Name='iCUE.exe' OR Name='iCUE Launcher.exe' OR Name='Corsair.Service.exe' OR Name='Corsair.Service.CpuIdRemote64.exe'"))
                foreach(ManagementObject o in search.Get()) {
                    string name=Text(o["Name"]);
                    if(name.Equals("GCC.exe",StringComparison.OrdinalIgnoreCase)) { s.GccPid=Convert.ToInt32(o["ProcessId"]); s.GccStartUtc=ManagementDateTimeConverter.ToDateTime(Text(o["CreationDate"])).ToUniversalTime().ToString("o"); }
                    else s.IcueRunning=true;
                }
                using(var search=new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%VID_1B1C%' AND Present=True")) s.OtherCorsairUsb=search.Get().Count>0;
            } catch(Exception ex) { s.Errors.Add("控制链路读取失败："+ex.Message); }
            try { s.GccAutostart=TaskControl.HasGccStartup(s.GccPath); } catch { s.GccAutostart=false; }
            try {
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Corsair","Logs","CorsairDeviceControlService");
                if(Directory.Exists(folder) && s.GccPid>0 && !String.IsNullOrEmpty(s.BootUtc)) {
                    var files=new DirectoryInfo(folder).GetFiles("*.log").OrderByDescending(f=>f.LastWriteTimeUtc).Take(5);
                    foreach(var f in files) {
                        string content=ReadBounded(f.FullName,1024*1024);
                        var evidence=LogParser.Parse(content,s.GccPid,DateTime.Parse(s.BootUtc).ToUniversalTime(),DateTime.Parse(s.GccStartUtc).ToUniversalTime());
                        if(evidence.CurrentSession) { evidence.FileName=f.Name; s.Evidence=evidence; break; }
                    }
                }
            } catch(Exception ex) { s.Errors.Add("海盗船日志读取失败："+ex.Message); }
            try {
                var parts=new List<string>{s.BoardVendor,s.BoardModel,s.OsVersion,s.GccVersion,s.StorageVersion,s.IcueVersion,s.DcsVersion};
                parts.AddRange(s.Ram.Select(r=>r.Part).OrderBy(v=>v));
                foreach(string path in new[]{s.GccPath,dcs,Path.Combine(GccDir,"Lib","MBStorage","CorsairDeviceControlServiceAPI.dll"),Path.Combine(GccDir,"Lib","MBStorage","MB.CPNT.Internal.LED.Generic.Corsair.dll"),Path.Combine(IcueDir,"CorsairLLAccess64.sys")}) {
                    if(!File.Exists(path)) throw new FileNotFoundException("控制组件缺失",path); parts.Add(Hash(path));
                }
                s.Fingerprint=HashBytes(Encoding.UTF8.GetBytes(String.Join("|",parts)));
            } catch { s.Fingerprint=null; }
            NativeProbe.Read(s);
            try { var e=StateStore.Load(); s.AssistantEnabled=e!=null && e.Enabled; s.AssistantState=e==null ? "未启用" : e.Enabled ? (e.Fingerprint==s.Fingerprint ? "已启用" : "版本变化，自动恢复暂停") : "已撤销";
                s.NativeConfirmed=e!=null && e.Enabled && e.Brand==s.Brand && e.Fingerprint==s.Fingerprint && !String.IsNullOrEmpty(e.NativeConfirmedUtc); if(s.NativeConfirmed) s.NativeConfirmedUtc=e.NativeConfirmedUtc; }
            catch(Exception ex) { s.AssistantState="设置读取失败"; s.Errors.Add(ex.Message); }
            Rules.Evaluate(s); return s;
        }
        public static string Text(object value) { return Convert.ToString(value).Trim(); }
        public static string NormalizeDriverPath(string value) {
            string s=value.Trim('"'); if(s.StartsWith("\\??\\")) s=s.Substring(4); return Environment.ExpandEnvironmentVariables(s);
        }
        public static string ReadBounded(string path,int bytes) {
            using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                if(f.Length>bytes) f.Seek(-bytes,SeekOrigin.End);
                using(var r=new StreamReader(f,Encoding.UTF8,true)) return r.ReadToEnd();
            }
        }
        public static string Hash(string path) { using(var f=File.OpenRead(path)) using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-",""); }
        public static string HashBytes(byte[] value) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(value)).Replace("-",""); }
        static string Version(Snapshot s,string name,bool prefix) { var app=s.Apps.FirstOrDefault(a=>prefix ? a.Name.StartsWith(name,StringComparison.OrdinalIgnoreCase) : a.Name.Equals(name,StringComparison.OrdinalIgnoreCase)); return app==null ? null : app.Version; }
        static void ReadApps(Snapshot s) {
            foreach(RegistryView view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
            using(var uninstall=root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall")) {
                if(uninstall==null) continue;
                foreach(string name in uninstall.GetSubKeyNames()) using(var key=uninstall.OpenSubKey(name)) {
                    if(key==null) continue; string display=Text(key.GetValue("DisplayName"));
                    if(Regex.IsMatch(display,"Corsair|iCUE|GIGABYTE|RGB|Fusion|Aura|Armoury|Mystic|MSI Center|Polychrome",RegexOptions.IgnoreCase))
                    if(!s.Apps.Any(a=>a.Name==display)) s.Apps.Add(new AppInfo{Name=display,Version=Text(key.GetValue("DisplayVersion"))});
                }
            }
        }
    }
}
