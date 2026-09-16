using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Management;
using System.Security.Cryptography.X509Certificates;

namespace CorsairTakeover {
    public static class NativeProbe {
        public static string Executable(string command) {
            string c=Environment.ExpandEnvironmentVariables(command??"").Trim();
            Match m=Regex.Match(c,"^\"([^\"]+\\.exe)\"(?:\\s|$)",RegexOptions.IgnoreCase);
            if(m.Success) return m.Groups[1].Value;
            m=Regex.Match(c,@"^([^\s""]+\.exe)(?:\s|$)",RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null; // Ambiguous unquoted paths are not executable targets.
        }
        public static bool PublisherMatches(string publisher,string brand) {
            string p=(publisher??"").ToUpperInvariant();
            if(brand=="ASUS") return p.Contains("ASUSTEK COMPUTER") || p=="ASUS";
            if(brand=="MSI") return p.Contains("MICRO-STAR") || p.Contains("MICRO STAR");
            return brand=="Corsair" && p.Contains("CORSAIR");
        }
        public static string Publisher(string path) {
            using(var c=new X509Certificate2(X509Certificate.CreateFromSignedFile(path))) return c.GetNameInfo(X509NameType.SimpleName,false);
        }
        public static bool ProtectedLocation(string path) {
            if(String.IsNullOrEmpty(path) || !Path.IsPathRooted(path) || !File.Exists(path)) return false;
            string full=Path.GetFullPath(path);
            bool under=new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}
                .Where(p=>!String.IsNullOrEmpty(p)).Any(p=>full.StartsWith(p.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase));
            if(!under) return false;
            for(string p=full; !String.IsNullOrEmpty(p); p=Path.GetDirectoryName(p)) {
                if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) return false;
            }
            return true;
        }
        static bool HasApp(Snapshot s,string pattern) { return s.Apps.Any(a=>Regex.IsMatch(a.Name,pattern,RegexOptions.IgnoreCase)); }
        public static bool HasAuraPlugin(Snapshot s) { return s.Apps.Any(a=>Regex.IsMatch(a.Name,"Corsair",RegexOptions.IgnoreCase)&&Regex.IsMatch(a.Name,"AURA",RegexOptions.IgnoreCase)); }
        public static bool HasMsiCenter(Snapshot s) { return HasApp(s,@"^MSI\s*Center(?:\s+SDK)?$"); }
        static void Check(Snapshot s,string name,bool present,string detail) { s.Requirements.Add(new ComponentCheck{Name=name,Present=present,Detail=detail}); }
        public static string Role(string brand,string file) {
            file=(file??"").ToLowerInvariant();
            if(brand=="ASUS" && file=="lightingservice.exe") return "Lighting";
            if(brand=="ASUS" && file=="armourycrateservice.exe") return "Core";
            if(brand=="MSI" && (file=="msi_central_service.exe" || file=="msi_center_service.exe")) return "Core";
            if(brand=="MSI" && (file=="mystic_light_service.exe" || file=="lightkeeperservice.exe")) return "Lighting";
            if(file=="corsair.service.exe") return "Corsair";
            return null;
        }
        public static void Read(Snapshot s) {
            if(!NativePolicy.Supports(s.Brand)) return;
            try {
                using(var q=new ManagementObjectSearcher("SELECT Name,DisplayName,PathName,State,StartMode,StartName FROM Win32_Service"))
                foreach(ManagementObject o in q.Get()) {
                    string command=Probe.Text(o["PathName"]), path=Executable(command);
                    // Find relevant candidates even if their command line is ambiguous; never execute the fallback.
                    string file=path==null ? Path.GetFileName(Regex.Match(command,@"(?i)^\s*""?(.+?\.exe)").Groups[1].Value) : Path.GetFileName(path);
                    string role=Role(s.Brand,file); if(role==null) continue;
                    string mode=Probe.Text(o["StartMode"]);
                    // Optional Corsair services are kept only when installed as automatic services.
                    if(role=="Corsair" && mode!="Auto") continue;
                    var v=new NativeService{Name=Probe.Text(o["Name"]),DisplayName=Probe.Text(o["DisplayName"]),Command=command,Path=path,State=Probe.Text(o["State"]),StartMode=mode,Account=Probe.Text(o["StartName"]),Role=role};
                    try { if(ProtectedLocation(path)) { v.Publisher=Publisher(path); v.Trusted=PublisherMatches(v.Publisher,role=="Corsair"?"Corsair":s.Brand)&&SignedFiles.Valid(path); v.Hash=Probe.Hash(path); } } catch { v.Trusted=false; }
                    s.NativeServices.Add(v);
                }
                if(s.Brand=="ASUS") {
                    Check(s,"Armoury Crate",HasApp(s,"Armoury\\s*Crate"),"检测安装记录；官方界面授权及 Memory 枚举需人工确认。");
                    Check(s,"Corsair Aura 内存插件",HasAuraPlugin(s),"检测插件安装记录；卸载 iCUE 后需按官方说明重装插件。");
                    Check(s,"Aura Lighting 服务",s.NativeServices.Any(v=>v.Role=="Lighting"),"从实际服务路径识别 LightingService.exe，校验 ASUS 签名。");
                    Check(s,"iCUE 接入方式",true,String.IsNullOrEmpty(s.IcueVersion)?"采用官方插件独立接入方式。":"保留 iCUE；在官方界面打开 Device Control Service / Approve。");
                } else {
                    Check(s,"MSI Center",HasMsiCenter(s) && s.NativeServices.Any(v=>v.Role=="Core"),"检测 MSI Center/SDK 安装记录及厂商核心服务；不接受仅有 Dragon Center 或 Center S。");
                    Check(s,"iCUE",!String.IsNullOrEmpty(s.IcueVersion),"官方 Mystic Light 内存流程要求安装 iCUE，保留现有启动配置。");
                    bool feature=HasApp(s,@"Mystic\s*Light") || s.NativeServices.Any(v=>v.Role=="Lighting"&&v.Trusted);
                    foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)}.Distinct()) {
                        foreach(string relative in new[]{@"MSI\MSI Center\Mystic Light\LEDKeeper2.exe",@"MSI\MSI Center\Mystic Light\Mystic_Light_Service.exe"}) {
                            string path=Path.Combine(root,relative);
                            if(File.Exists(path) && ProtectedLocation(path) && SignedFiles.Valid(path) && PublisherMatches(Publisher(path),"MSI")) { feature=true; s.ComponentFiles.Add(path); }
                        }
                    }
                    Check(s,"Mystic Light 功能",feature,"识别已签名的灯效组件或安装记录；未识别的安装布局需导出报告。");
                    // Some MSI Center builds host Mystic Light in their central service rather than a dedicated one.
                    if(feature && !s.NativeServices.Any(v=>v.Role=="Lighting")) foreach(var v in s.NativeServices.Where(v=>v.Role=="Core")) v.Role="Lighting";
                }
                s.NativeServices=s.NativeServices.OrderBy(v=>v.Role=="Corsair"?0:v.Role=="Core"?1:2).ThenBy(v=>v.Name).ToList();
                var parts=new List<string>{s.Brand,s.BoardVendor,s.BoardModel,s.OsVersion};
                parts.AddRange(s.Ram.Select(r=>r.Part+":"+r.Bytes).OrderBy(v=>v));
                parts.AddRange(s.Apps.OrderBy(a=>a.Name).Select(a=>a.Name+":"+a.Version));
                foreach(var v in s.NativeServices) parts.Add(v.Name+"|"+v.Command+"|"+v.Account+"|"+v.StartMode+"|"+v.Hash+"|"+v.Role);
                foreach(string p in s.ComponentFiles.OrderBy(p=>p)) parts.Add(p+":"+Probe.Hash(p));
                s.Fingerprint=Probe.HashBytes(Encoding.UTF8.GetBytes(String.Join("\n",parts)));
            } catch(Exception ex) { s.Errors.Add("品牌组件检测失败："+ex.Message); s.Fingerprint=null; }
        }
    }
}
