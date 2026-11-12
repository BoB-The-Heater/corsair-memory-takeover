using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;

namespace CorsairTakeover {
    internal interface IGigabyteConnection {
        DateTime Restart(Snapshot baseline);
        Snapshot Observe(Snapshot baseline);
        Snapshot Verify();
        TimeSpan Elapsed { get; }
        void Wait(TimeSpan delay);
    }
    public static class GigabyteRecovery {
        internal static readonly TimeSpan ConfirmationTimeout=TimeSpan.FromSeconds(38);
        static readonly TimeSpan PollInterval=TimeSpan.FromMilliseconds(500);
        public static string ConnectionBlockReason(Snapshot s) {
            if(s==null) return "请先完成检测。";
            if(s.Brand!="GIGABYTE") return "此连接流程仅适用于指定技嘉组合。";
            if(!String.Equals(s.BoardModel,"B550 VISION D-P",StringComparison.OrdinalIgnoreCase)) return "手动连接目前仅适配 B550 VISION D-P。";
            if(s.Ram.Count!=2 || s.Ram.Any(r=>(r.Part??"").Trim()!="CMT32GX4M2D3600C18")) return "手动连接目前仅适配两条 CMT32GX4M2D3600C18 内存。";
            if(s.GccVersion!="26.03.26.01" || s.StorageVersion!="26.04.27.02" || s.DcsVersion!="1.9.15" || s.IcueVersion!="5.46.67") return "软件版本与已知连接组合不同，请按官方步骤接入。";
            if(!(s.OsVersion??"").StartsWith("10.0.28000",StringComparison.Ordinal)) return "Windows 版本与已知连接环境不同。";
            if(s.Errors.Count>0 || String.IsNullOrEmpty(s.Fingerprint)) return "检测信息不完整，请重新检测并查看缺项。";
            if(s.IcueRunning) return "请先自行退出 iCUE，再连接内存。";
            if(s.OtherCorsairUsb) return "检测到其他海盗船 USB 设备，请先确认其对 iCUE 的依赖。";
            if(s.IcueDrivers.Count!=1 || !ServiceProbe.IsIcueDriver(s.IcueDrivers[0].Name,s.IcueDrivers[0].Path)) return "iCUE 驱动名称或路径与已知组合不同。";
            if(s.IcueDrivers[0].Start!=2 && s.IcueDrivers[0].Start!=3) return "驱动启动设置超出支持范围，请在官方软件中修复。";
            if(s.IcueDrivers[0].State!="Running" && s.IcueDrivers[0].State!="Stopped") return "驱动正在切换状态，请稍后重新检测。";
            if(s.DcsStart!=2 && s.DcsStart!=3) return "设备服务启动设置超出支持范围，请在官方软件中修复。";
            if(s.DcsState!="Running" && s.DcsState!="Stopped") return "设备服务正在切换状态或未安装。";
            if(!String.Equals(s.GccPath,Path.Combine(Probe.GccDir,"GCC.exe"),StringComparison.OrdinalIgnoreCase)) return "GCC 路径与已知安装位置不同。";
            return null;
        }
        public static bool CanConnect(Snapshot s) { return ConnectionBlockReason(s)==null; }
        static bool Utc(string text,out DateTime value) {
            return DateTime.TryParse(text,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out value) && value.Kind==DateTimeKind.Utc;
        }
        static DateTime LogSecond(DateTime value) { return value.AddTicks(-(value.Ticks%TimeSpan.TicksPerSecond)); }
        public static bool IsConnected(Snapshot s) {
            if(s==null || s.Brand!="GIGABYTE" || s.Errors.Count>0 || String.IsNullOrEmpty(s.Fingerprint) || s.GccPid<=0 || s.DcsState!="Running" || s.IcueRunning || s.OtherCorsairUsb) return false;
            if(s.Ram.Count==0 || !s.Ram.All(Rules.IsCorsair) || !s.Evidence.CurrentSession || !s.Evidence.Connected || s.Evidence.Modules!=s.Ram.Count || s.Evidence.InstalledWriteFailures!=0 || s.Evidence.PipeErrors!=0) return false;
            DateTime boot,start,session;
            return Utc(s.BootUtc,out boot) && Utc(s.GccStartUtc,out start) && Utc(s.Evidence.SessionUtc,out session) &&
                start>=boot && session>=LogSecond(boot) && session>=LogSecond(start);
        }
        static bool SameProcess(Snapshot left,Snapshot right) {
            return left.GccPid>0 && left.GccPid==right.GccPid && left.GccStartUtc==right.GccStartUtc;
        }
        internal static void CheckEnvironment(Snapshot s) {
            if(s.Errors.Count>0) throw new InvalidOperationException("连接信息读取失败："+String.Join("；",s.Errors));
            if(s.IcueRunning || s.OtherCorsairUsb) throw new InvalidOperationException("发现 iCUE 或其他海盗船设备占用，已停止本次连接。");
        }
        internal static void CheckObservation(Snapshot s) {
            CheckEnvironment(s);
            if(!String.IsNullOrEmpty(s.Evidence.Note)) throw new InvalidOperationException("当前连接会话归属已改变："+s.Evidence.Note);
            if(s.Evidence.InstalledWriteFailures>0 || s.Evidence.PipeErrors>0) throw new InvalidOperationException("新连接会话出现内存写入或通信错误，请查看检测详情。");
        }
        internal static string Run(Snapshot baseline,IGigabyteConnection connection) {
            string blocked=ConnectionBlockReason(baseline);
            if(blocked!=null) throw new InvalidOperationException(blocked);
            if(IsConnected(baseline)) return "当前连接已确认，无需重新连接。";
            DateTime launched=connection.Restart(baseline);
            Snapshot process=null;
            while(connection.Elapsed<ConfirmationTimeout) {
                Snapshot current=connection.Observe(baseline);
                CheckEnvironment(current);
                if(current.GccPid>0) {
                    DateTime start;
                    if(!Utc(current.GccStartUtc,out start) || start<launched || SameProcess(current,baseline)) throw new InvalidOperationException("尚未确认本次启动的新 GCC 会话，已停止连接确认。");
                    if(process==null) process=current;
                    else if(!SameProcess(current,process)) throw new InvalidOperationException("GCC 进程在连接期间发生变化，请重新检测。");
                    CheckObservation(current);
                    if(IsConnected(current)) {
                        Snapshot verified=connection.Verify();
                        CheckObservation(verified);
                        if(verified.Fingerprint!=baseline.Fingerprint || !CanConnect(verified) || !SameProcess(verified,process) || !IsConnected(verified))
                            throw new InvalidOperationException("连接后的完整检测与当前会话不一致，未确认成功。");
                        return "已确认新的 GCC 会话连接 "+verified.Evidence.Modules+" 条内存。请在官方软件中核实实际灯效。";
                    }
                }
                TimeSpan remaining=ConfirmationTimeout-connection.Elapsed;
                if(remaining<=TimeSpan.Zero) break;
                connection.Wait(remaining<PollInterval?remaining:PollInterval);
            }
            throw new System.TimeoutException("等待新的 GCC 内存连接日志超时，未确认成功。请查看检测详情，未再次重启服务。");
        }
        static void ValidateTargets(Snapshot s) {
            if(!StateStore.Admin) throw new InvalidOperationException("需要管理员权限。");
            string blocked=ConnectionBlockReason(s);
            if(blocked!=null) throw new InvalidOperationException(blocked);
            foreach(string path in Probe.ControlFiles(s))
                if(!NativeProbe.ProtectedLocation(path)) throw new InvalidOperationException("控制组件路径校验失败："+Path.GetFileName(path));
            foreach(string path in new[]{s.GccPath,Path.Combine(Probe.DcsDir,"CorsairDeviceControlService.exe"),s.IcueDrivers[0].Path})
                if(!SignedFiles.Valid(path)) throw new InvalidOperationException("控制组件签名校验失败："+Path.GetFileName(path));
            if(Probe.ControlFingerprint(s)!=s.Fingerprint) throw new InvalidOperationException("控制组件在检测后发生变化，请重新检测。");
            var service=WindowsServiceStarter.ReadCurrentService("CorsairDeviceControlService");
            if(service==null || !String.Equals(NativeProbe.Executable(service.Command),Path.Combine(Probe.DcsDir,"CorsairDeviceControlService.exe"),StringComparison.OrdinalIgnoreCase) || service.StartMode!=(s.DcsStart==2?"Auto":"Manual"))
                throw new InvalidOperationException("设备服务配置在执行前校验失败。");
            using(var key=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\"+s.IcueDrivers[0].Name)) {
                if(key==null || Convert.ToInt32(key.GetValue("Start",-1))!=s.IcueDrivers[0].Start || !String.Equals(Probe.NormalizeDriverPath(Probe.Text(key.GetValue("ImagePath"))),s.IcueDrivers[0].Path,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("驱动配置在执行前发生变化。");
            }
        }
        internal static string Connect(Snapshot s) {
            ValidateTargets(s);
            StateStore.Audit("开始用户请求的一次性技嘉内存连接检查。");
            string result=Run(s,new WindowsGigabyteConnection());
            StateStore.Audit(result);
            return result;
        }
    }
    internal sealed class WindowsGigabyteConnection : IGigabyteConnection {
        const string DcsName="CorsairDeviceControlService";
        readonly Stopwatch clock=new Stopwatch();
        public TimeSpan Elapsed { get { return clock.Elapsed; } }
        public Snapshot Observe(Snapshot baseline) { return Probe.ObserveGccConnection(baseline); }
        public Snapshot Verify() { return Probe.Scan(); }
        public void Wait(TimeSpan delay) { Thread.Sleep(delay); }
        public DateTime Restart(Snapshot baseline) {
            Snapshot current=Observe(baseline);
            GigabyteRecovery.CheckEnvironment(current);
            bool gccStopped=false,dcsStopped=false,driverStopped=false;
            try {
                StopGcc(baseline,current,ref gccStopped);
                StopExact(DcsName,ref dcsStopped);
                current=Observe(baseline);
                GigabyteRecovery.CheckEnvironment(current);
                StopExact(baseline.IcueDrivers[0].Name,ref driverStopped);
                StartExact(DcsName);
                DateTime launched=DateTime.UtcNow;
                LaunchGcc(baseline.GccPath);
                clock.Restart();
                return launched;
            } catch(Exception failure) {
                var errors=new List<Exception>{failure};
                if(driverStopped && baseline.IcueDrivers[0].State=="Running") Restore(()=>StartExact(baseline.IcueDrivers[0].Name),errors);
                if(dcsStopped && baseline.DcsState=="Running") Restore(()=>StartExact(DcsName),errors);
                if(gccStopped) Restore(()=>LaunchGcc(baseline.GccPath),errors);
                if(errors.Count>1) throw new AggregateException("连接未完成，恢复原运行状态时也发生错误，请查看检测详情。",errors);
                throw;
            }
        }
        static void Restore(Action restore,List<Exception> errors) {
            try { restore(); } catch(Exception ex) { errors.Add(ex); }
        }
        static void StopGcc(Snapshot baseline,Snapshot current,ref bool touched) {
            if(current.GccPid==0) return;
            if(current.GccPid!=baseline.GccPid || current.GccStartUtc!=baseline.GccStartUtc) throw new InvalidOperationException("GCC 进程在检测后发生变化，请重新检测。");
            using(var process=Process.GetProcessById(current.GccPid)) {
                IntPtr handle=process.Handle;
                DateTime expected=DateTime.Parse(current.GccStartUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
                if(process.StartTime.ToUniversalTime().Ticks/10!=expected.Ticks/10 || !String.Equals(process.MainModule.FileName,baseline.GccPath,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("GCC 进程身份与已验证安装位置不符。");
                touched=true;
                process.Kill();
                if(!process.WaitForExit(8000)) throw new System.TimeoutException("等待 GCC 退出超时，未继续操作服务。");
            }
        }
        static void LaunchGcc(string path) {
            var processes=Process.GetProcessesByName("GCC");
            try { if(processes.Length!=0) throw new InvalidOperationException("已有 GCC 进程出现，请重新检测，未重复启动。"); }
            finally { foreach(var process in processes) process.Dispose(); }
            using(var process=Process.Start(new ProcessStartInfo(path,"-b") { WorkingDirectory=Path.GetDirectoryName(path),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden })) {
                if(process==null) throw new InvalidOperationException("GCC 启动未返回进程，未确认连接。");
            }
        }
        static void StartExact(string name) {
            using(var controller=new ServiceController(name)) {
                controller.Refresh();
                if(controller.Status==ServiceControllerStatus.StopPending) {
                    controller.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(12));
                    controller.Refresh();
                }
                if(controller.Status==ServiceControllerStatus.Running) return;
                if(controller.Status==ServiceControllerStatus.Stopped) controller.Start();
                else if(controller.Status!=ServiceControllerStatus.StartPending) throw new InvalidOperationException("服务状态发生变化："+name);
                controller.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(12));
            }
        }
        [StructLayout(LayoutKind.Sequential)] struct ServiceStatus {
            public uint Type,CurrentState,ControlsAccepted,Win32ExitCode,ServiceSpecificExitCode,CheckPoint,WaitHint;
        }
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenSCManager(string machine,string database,uint access);
        [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr OpenService(IntPtr manager,string name,uint access);
        [DllImport("advapi32.dll",SetLastError=true)] static extern bool ControlService(IntPtr service,uint control,out ServiceStatus status);
        [DllImport("advapi32.dll",SetLastError=true)] static extern bool CloseServiceHandle(IntPtr handle);
        internal static void StopAndWait(Action request,Action wait,ref bool touched) {
            touched=true;
            request();
            wait();
        }
        static void StopExact(string name,ref bool touched) {
            using(var controller=new ServiceController(name)) {
                controller.Refresh();
                if(controller.Status==ServiceControllerStatus.Stopped) return;
                if(controller.Status==ServiceControllerStatus.StopPending) {
                    controller.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(12));
                    return;
                }
                if(controller.Status!=ServiceControllerStatus.Running && controller.Status!=ServiceControllerStatus.Paused) throw new InvalidOperationException("服务状态发生变化："+name);
                IntPtr manager=OpenSCManager(null,null,1);
                if(manager==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法访问服务管理器。");
                try {
                    IntPtr service=OpenService(manager,name,0x20);
                    if(service==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),"无法停止目标服务："+name);
                    try {
                        StopAndWait(delegate {
                            ServiceStatus status;
                            if(!ControlService(service,1,out status)) {
                                int error=Marshal.GetLastWin32Error();
                                if(error!=1062) throw new Win32Exception(error,"目标服务拒绝停止："+name);
                            }
                        },()=>controller.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(12)),ref touched);
                    } finally { CloseServiceHandle(service); }
                } finally { CloseServiceHandle(manager); }
            }
        }
    }
}
