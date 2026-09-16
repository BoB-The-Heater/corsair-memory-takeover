using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CorsairTakeover {
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                if(args.Length==2 && (args[0]=="--scan" || args[0]=="--plan" || args[0]=="--preflight")) {
                    if(File.Exists(args[1])) throw new IOException("输出文件已存在，拒绝覆盖。");
                    Snapshot s=Probe.Scan(); string value=args[0]=="--scan"?Json.Write(s):args[0]=="--plan"?Operations.Plan(s):Json.Write(new{Brand=s.Brand,Rule=s.RuleEligible,Reason=s.RuleReason,Fingerprint=s.Fingerprint,NativeServices=s.NativeServices,Requirements=s.Requirements,CanEnrollNative=NativePolicy.Supports(s.Brand)&&NativePolicy.CanEnroll(s),GccSigned=File.Exists(s.GccPath)&&SignedFiles.Valid(s.GccPath),DcsSigned=File.Exists(Path.Combine(Probe.DcsDir,"CorsairDeviceControlService.exe"))&&SignedFiles.Valid(Path.Combine(Probe.DcsDir,"CorsairDeviceControlService.exe")),TaskAvailable=s.GccAutostart,StateExists=File.Exists(StateStore.StateFile)});
                    File.WriteAllText(args[1],value,Encoding.UTF8); return 0;
                }
                if(args.Length==1 && args[0]=="--watch") {
                    if(!StateStore.Admin) return 2; StateStore.Prepare(); var e=StateStore.Load(); if(e==null||!e.Enabled||e.OwnerSid!=StateStore.Sid) return 3;
                    using(var mutex=new Mutex(false,"Local\\CorsairTakeoverWatch-"+StateStore.Sid)) {
                        bool held=false; try { held=mutex.WaitOne(0); } catch(AbandonedMutexException) { held=true; }
                        if(!held) return 0; try { Application.Run(new Watcher()); } finally { mutex.ReleaseMutex(); }
                    } return 0;
                }
                bool confirmedEnable=args.Length==2 && args[0]=="--enable-confirmed" && System.Text.RegularExpressions.Regex.IsMatch(args[1],"^[A-Fa-f0-9]{64}$");
                if(confirmedEnable || (args.Length==1 && (args[0]=="--enable"||args[0]=="--repair"||args[0]=="--restore"))) {
                    if(!StateStore.Admin) return 2; StateStore.Prepare(); bool success=false; string message;
                    using(var mutex=new Mutex(false,"Local\\CorsairTakeoverOperation-"+StateStore.Sid)) {
                        bool held=false; try { held=mutex.WaitOne(2000); } catch(AbandonedMutexException) { held=true; }
                        if(!held) { message="另一个恢复操作正在执行，请稍后重试。"; }
                        else { try { message=confirmedEnable?Operations.Enable(args[1]):args[0]=="--enable"?Operations.Enable():args[0]=="--repair"?Operations.Repair():Operations.Restore(); success=true; } catch(Exception ex) { message=ex.Message; StateStore.Audit("操作未完成："+ex.ToString()); } finally { mutex.ReleaseMutex(); } }
                    }
                    File.WriteAllText(Path.Combine(StateStore.Root,"last-result-"+StateStore.Sid+".json"),Json.Write(new OperationResult{Success=success,Message=message,AtUtc=DateTime.UtcNow.ToString("o")}),Encoding.UTF8); return success?0:1;
                }
                if(args.Length!=0) return 2;
                Application.Run(new MainForm()); return 0;
            } catch(Exception ex) {
                if(args.Length==0) MessageBox.Show(ex.Message,"接管助手未能启动");
                else if(args.Length==2 && (args[0]=="--scan" || args[0]=="--plan" || args[0]=="--preflight") && !File.Exists(args[1])) { try { File.WriteAllText(args[1],Json.Write(new OperationResult{Success=false,Message=ex.Message,AtUtc=DateTime.UtcNow.ToString("o")})); } catch { } }
                return 1;
            }
        }
    }
    public sealed class Watcher : ApplicationContext {
        NotifyIcon tray; Control dispatcher; int checking; DateTime lastEvent=DateTime.MinValue;
        public Watcher() {
            dispatcher=new Control(); IntPtr handle=dispatcher.Handle;
            tray=new NotifyIcon { Icon=System.Drawing.SystemIcons.Application,Text="海盗船接管助手 · 等待检查",Visible=true };
            var menu=new ContextMenuStrip(); menu.Items.Add("打开接管助手",null,delegate { Open(); }); menu.Items.Add("立即检查",null,delegate { QueueCheck("用户检查",0); }); menu.Items.Add("退出本次后台检查",null,delegate { ExitThread(); }); tray.ContextMenuStrip=menu; tray.DoubleClick+=delegate { Open(); };
            SystemEvents.PowerModeChanged+=PowerChanged; QueueCheck("登录检查",15000);
        }
        void Open() { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Normal}); }
        void PowerChanged(object sender,PowerModeChangedEventArgs args) { if(args.Mode==PowerModes.Resume) QueueCheck("唤醒检查",20000); }
        void QueueCheck(string reason,int delay) {
            if(DateTime.UtcNow-lastEvent<TimeSpan.FromSeconds(15)) return; lastEvent=DateTime.UtcNow;
            Task.Run(async delegate { await Task.Delay(delay); if(Interlocked.Exchange(ref checking,1)!=0) return;
                try { Check(reason); } catch(Exception ex) { try { StateStore.Audit("后台检查失败："+ex.Message); } catch { } Status("检查失败，请打开助手"); } finally { Interlocked.Exchange(ref checking,0); }
            });
        }
        void Check(string reason) {
            using(var mutex=new Mutex(false,"Local\\CorsairTakeoverOperation-"+StateStore.Sid)) {
                bool held=false; try { held=mutex.WaitOne(1000); } catch(AbandonedMutexException) { held=true; } if(!held) return;
                try {
                    var e=StateStore.Load(); if(e==null||!e.Enabled||e.OwnerSid!=StateStore.Sid) { dispatcher.BeginInvoke(new Action(()=>ExitThread())); return; }
                    Snapshot s=Probe.Scan(); DateTime now=DateTime.UtcNow;
                    if(s.Fingerprint!=e.Fingerprint) { e.LastCheck="软件或硬件变化，自动恢复暂停"; StateStore.Save(e); StateStore.Audit(e.LastCheck); Status(e.LastCheck); return; }
                    if(Rules.CanAutoRepair(s,e,now)) {
                        if(e.RepairDate!=now.ToString("yyyy-MM-dd")) { e.RepairDate=now.ToString("yyyy-MM-dd"); e.RepairsToday=0; }
                        e.RepairsToday++; e.LastRepairUtc=now.ToString("o"); StateStore.Save(e);
                        StateStore.Audit(reason+"："+(NativePolicy.Supports(s.Brand)?"基线中的官方自动服务停止":"当前会话有明确故障")+"，开始一次恢复。次数="+e.RepairsToday);
                        try { e.LastCheck=Operations.Reconnect(s); } catch(Exception ex) { e.LastCheck="自动恢复未确认成功："+ex.Message; }
                    } else e.LastCheck=reason+"："+s.Health+(s.IcueRunning && s.Brand=="GIGABYTE"?"；iCUE 正在运行，未执行恢复":"");
                    StateStore.Save(e); StateStore.Audit(e.LastCheck); Status(e.LastCheck);
                } finally { mutex.ReleaseMutex(); }
            }
        }
        void Status(string value) { if(dispatcher.IsDisposed) return; try { dispatcher.BeginInvoke(new Action(delegate { tray.Text=value.Length>60?value.Substring(0,60):value; })); } catch { } }
        protected override void ExitThreadCore() { SystemEvents.PowerModeChanged-=PowerChanged; tray.Visible=false; tray.Dispose(); dispatcher.Dispose(); base.ExitThreadCore(); }
    }
}
