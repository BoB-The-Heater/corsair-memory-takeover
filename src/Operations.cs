using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CorsairTakeover {
    public static class TaskControl {
        const string Marker="CorsairTakeoverAssistant.v02";
        public static string Name { get { return "CorsairTakeoverAssistant-"+StateStore.Sid; } }
        static dynamic Scheduler() { dynamic s=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")); s.Connect(); return s; }
        public static bool HasGccStartup(string exe) {
            try { dynamic s=Scheduler(); dynamic t=s.GetFolder("\\").GetTask("GCC"); if(!(bool)t.Enabled) return false;
                foreach(dynamic a in t.Definition.Actions) if((int)a.Type==0 && String.Equals(((string)a.Path).Trim('"'),exe,StringComparison.OrdinalIgnoreCase)) return true;
            } catch { } return false;
        }
        public static bool Exists() { try { dynamic s=Scheduler(); s.GetFolder("\\").GetTask(Name); return true; } catch(Exception ex) { if((uint)ex.HResult==0x80070002 || (uint)ex.HResult==0x8004130F) return false; throw; } }
        public static void Register(string exe) {
            dynamic s=Scheduler(); dynamic root=s.GetFolder("\\");
            if(Exists()) throw new InvalidOperationException("同名启动任务已存在；请先撤销旧配置。");
            dynamic d=s.NewTask(0); d.RegistrationInfo.Description="海盗船内存官方软件接管助手：登录和唤醒后检查；不设置灯效。"; d.RegistrationInfo.Source=Marker;
            d.Principal.UserId=StateStore.Sid; d.Principal.LogonType=3; d.Principal.RunLevel=1;
            d.Settings.Enabled=true; d.Settings.AllowDemandStart=true; d.Settings.StartWhenAvailable=true;
            d.Settings.DisallowStartIfOnBatteries=false; d.Settings.StopIfGoingOnBatteries=false; d.Settings.MultipleInstances=2; d.Settings.ExecutionTimeLimit="PT0S";
            dynamic trigger=d.Triggers.Create(9); trigger.UserId=StateStore.Sid; trigger.Delay="PT25S"; trigger.Enabled=true;
            dynamic action=d.Actions.Create(0); action.Path=exe; action.Arguments="--watch"; action.WorkingDirectory=StateStore.Root;
            root.RegisterTaskDefinition(Name,d,2,StateStore.Sid,null,3,null);
        }
        static dynamic OwnedTask() {
            dynamic s=Scheduler(); dynamic task=s.GetFolder("\\").GetTask(Name);
            string marker=(string)task.Definition.RegistrationInfo.Source;
            if(marker!=Marker && marker!="CorsairTakeoverAssistant.v01") throw new InvalidOperationException("同名任务不属于本工具，拒绝修改。");
            return task;
        }
        public static void Start() { dynamic task=OwnedTask(); task.Run(null); }
        public static void Remove() { if(!Exists()) return; dynamic task=OwnedTask(); try { task.Stop(0); } catch { } dynamic s=Scheduler(); s.GetFolder("\\").DeleteTask(Name,0); }
    }
    public static class SignedFiles {
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileInfo { public int Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File; public IntPtr Subject; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct TrustData { public int Size; public IntPtr Policy; public IntPtr Sip; public int UI; public int Revocation; public int Choice; public IntPtr File; public int StateAction; public IntPtr State; public IntPtr Url; public int Flags; public int Context; public IntPtr SignatureSettings; }
        [DllImport("wintrust.dll",ExactSpelling=true,SetLastError=false)] static extern int WinVerifyTrust(IntPtr hwnd,[In] ref Guid action,ref TrustData data);
        public static bool Valid(string path) {
            FileInfo info=new FileInfo { Size=Marshal.SizeOf(typeof(FileInfo)),Path=path };
            IntPtr ptr=Marshal.AllocHGlobal(info.Size);
            try { Marshal.StructureToPtr(info,ptr,false); TrustData data=new TrustData { Size=Marshal.SizeOf(typeof(TrustData)),UI=2,Choice=1,File=ptr,Flags=0x1010 };
                Guid action=new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); return WinVerifyTrust(new IntPtr(-1),ref action,ref data)==0;
            } finally { Marshal.DestroyStructure(ptr,typeof(FileInfo)); Marshal.FreeHGlobal(ptr); }
        }
    }
    public static class Operations {
        public static string Plan(Snapshot s) {
            if(NativePolicy.Supports(s.Brand)) return NativePolicy.Plan(s);
            return "此品牌仅提供检测与接入指引，不执行自动恢复或启动配置修改。";
        }
        static void Guard(Snapshot s) {
            if(!NativePolicy.Supports(s.Brand)) throw new InvalidOperationException("此品牌仅提供检测与接入指引。");
            if(!StateStore.Admin) throw new InvalidOperationException("需要管理员权限。");
            if(!s.RuleEligible) throw new InvalidOperationException(s.RuleReason);
            foreach(var v in s.NativeServices)
                if(!v.Trusted || !NativeProbe.ProtectedLocation(v.Path) || !SignedFiles.Valid(v.Path) || Probe.Hash(v.Path)!=v.Hash)
                    throw new InvalidOperationException("官方服务组件校验失败："+v.Name);
        }
        public static string Enable(string confirmedFingerprint=null) {
            Snapshot s=Probe.Scan(); Guard(s);
            if(confirmedFingerprint!=s.Fingerprint || !NativePolicy.CanEnroll(s))
                throw new InvalidOperationException("请先在官方软件确认内存可控，并在全部目标服务运行时启用。");
            StateStore.Prepare(); Enrollment old=StateStore.Load();
            if(old!=null && old.OwnerSid!=StateStore.Sid) throw new InvalidOperationException("已有其他用户的配置，不能覆盖。");
            if(old!=null && old.Enabled) return "已经启用。更新基线前请先撤销，再重新启用。";
            if(old!=null && old.Changes.Count>0) throw new InvalidOperationException("上一份备份尚有未恢复项目；请先完成撤销。");
            if(TaskControl.Exists()) throw new InvalidOperationException("同名任务已存在，请先撤销旧配置。");
            string current=Assembly.GetExecutingAssembly().Location;
            var e=new Enrollment {
                Schema=2,Brand=s.Brand,Enabled=false,OwnerSid=StateStore.Sid,
                EnrolledUtc=DateTime.UtcNow.ToString("o"),Fingerprint=s.Fingerprint,TaskName=TaskControl.Name,
                InstalledExe=Path.Combine(StateStore.Root,"CorsairTakeover-0.3.1-"+Probe.Hash(current).Substring(0,16)+".exe"),
                NativeConfirmedUtc=DateTime.UtcNow.ToString("o"),BaselineServices=s.NativeServices.Select(v=>v.Name).ToList()
            };
            if(!String.Equals(current,e.InstalledExe,StringComparison.OrdinalIgnoreCase)) {
                if(File.Exists(e.InstalledExe)) {
                    if((File.GetAttributes(e.InstalledExe)&FileAttributes.ReparsePoint)!=0 || Probe.Hash(current)!=Probe.Hash(e.InstalledExe))
                        throw new InvalidOperationException("安装文件校验失败。");
                } else File.Copy(current,e.InstalledExe,false);
            }
            if(old!=null) File.Copy(StateStore.StateFile,Path.Combine(StateStore.Root,"state-before-"+Guid.NewGuid().ToString("N")+".json"),false);
            StateStore.Save(e);
            bool createdTask=false;
            try {
                TaskControl.Register(e.InstalledExe); createdTask=true; e.Enabled=true; StateStore.Save(e);
                StateStore.Audit("服务维护已启用。规则="+NativePolicy.RuleName(s.Brand));
            } catch {
                if(createdTask) TaskControl.Remove();
                e.Enabled=false; StateStore.Save(e); throw;
            }
            try { TaskControl.Start(); }
            catch(Exception ex) { StateStore.Audit("立即启动检查失败："+ex.Message); return "维护已保存；下次登录将重试启动检查。"; }
            return "已保存接管基线并启用服务维护。请在重启、唤醒后核实实际同步。";
        }
        public static string Restore() {
            StateStore.Prepare(); Enrollment e=StateStore.Load(); if(e==null) return "本工具没有持久设置可撤销。";
            if(e.OwnerSid!=StateStore.Sid) throw new InvalidOperationException("仅原启用用户可撤销。");
            e.Enabled=false; StateStore.Save(e); TaskControl.Remove();
            var store=new WindowsValues(); var pending=new List<RegistryChange>();
            foreach(var c in e.Changes) { try { if(!Changes.Restore(store,c)) pending.Add(c); } catch { pending.Add(c); } }
            e.Changes=pending; StateStore.Save(e); StateStore.Audit("已撤销持久接管；需手动处理项目="+pending.Count);
            return pending.Count==0 ? "已删除本工具启动任务并恢复本工具修改的设置。保留安装文件与记录；不会重启电脑。" : "已停止后台检查。部分设置被其他程序改变，已保留它们，详情见备份和记录。";
        }
        public static string Repair() {
            Snapshot s=Probe.Scan(); Guard(s); StateStore.Prepare();
            return NativeRecovery.Run(s,StateStore.Load(),new WindowsServiceStarter());
        }
        public static string Reconnect(Snapshot s) {
            Snapshot fresh=Probe.Scan();
            if(fresh.Fingerprint!=s.Fingerprint) throw new InvalidOperationException("组件已变化，请重新检测。");
            Guard(fresh);
            return NativeRecovery.Run(fresh,StateStore.Load(),new WindowsServiceStarter());
        }
    }
}
