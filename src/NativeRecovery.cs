using System;
using System.Linq;
using System.Collections.Generic;
using System.Management;
using System.ServiceProcess;

namespace CorsairTakeover {
    public interface IServiceStarter { void StartStopped(NativeService service); }
    public static class NativeRecovery {
        public static string Run(Snapshot s,Enrollment e,IServiceStarter starter) {
            if(e==null || !e.Enabled || e.OwnerSid!=StateStore.Sid || e.Brand!=s.Brand || e.Fingerprint!=s.Fingerprint || String.IsNullOrEmpty(e.NativeConfirmedUtc) || !s.RuleEligible)
                throw new InvalidOperationException("没有与当前环境匹配的官方接管基线。请先完成官方接管验证，再启用持久维护。");
            var names=NativePolicy.ToStart(s,e);
            if(names.Count==0) return "基线服务均无可恢复的停止项。服务运行不代表内存已接管；请在官方软件核实授权、插件及同步组。";
            foreach(string name in names) starter.StartStopped(s.NativeServices.Single(v=>v.Name==name));
            return "已恢复停止的官方服务："+String.Join("、",names)+"。请在官方软件核实内存是否重新出现；未检测或改变灯效。";
        }
    }
    public sealed class WindowsServiceStarter : IServiceStarter {
        public void StartStopped(NativeService expected) {
            // Read again immediately before SCM Start; never stop or restart running services.
            NativeService actual=null;
            using(var q=new ManagementObjectSearcher("SELECT Name,PathName,StartMode,StartName FROM Win32_Service"))
            foreach(ManagementObject o in q.Get()) if(Probe.Text(o["Name"])==expected.Name) actual=new NativeService{Name=expected.Name,Command=Probe.Text(o["PathName"]),StartMode=Probe.Text(o["StartMode"]),Account=Probe.Text(o["StartName"])};
            if(actual==null || actual.Command!=expected.Command || actual.StartMode!="Auto" || actual.Account!=expected.Account || !NativeProbe.ProtectedLocation(expected.Path) || !SignedFiles.Valid(expected.Path) || Probe.Hash(expected.Path)!=expected.Hash)
                throw new InvalidOperationException("服务执行前校验失败："+expected.Name);
            using(var controller=new ServiceController(expected.Name)) {
                controller.Refresh(); if(controller.Status==ServiceControllerStatus.Running) return;
                if(controller.Status!=ServiceControllerStatus.Stopped) throw new InvalidOperationException("服务状态正在变化，停止本次恢复："+expected.Name);
                StateStore.Audit("开始恢复已确认的官方自动服务："+expected.Name);
                controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(15));
                StateStore.Audit("官方服务已运行，内存接管仍待核实："+expected.Name);
            }
        }
    }
}
