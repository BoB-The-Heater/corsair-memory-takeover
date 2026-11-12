using System;
using System.IO;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CorsairTakeover {
    internal static class ServiceProbe {
        internal static string State(ServiceControllerStatus status) {
            switch(status) {
                case ServiceControllerStatus.Stopped: return "Stopped";
                case ServiceControllerStatus.StartPending: return "Start Pending";
                case ServiceControllerStatus.StopPending: return "Stop Pending";
                case ServiceControllerStatus.Running: return "Running";
                case ServiceControllerStatus.ContinuePending: return "Continue Pending";
                case ServiceControllerStatus.PausePending: return "Pause Pending";
                case ServiceControllerStatus.Paused: return "Paused";
                default: throw new InvalidOperationException("无法识别服务管理器返回的状态。");
            }
        }
        internal static bool IsIcueDriver(string name,string path) {
            return Regex.IsMatch(name,"^CorsairLLAccess[0-9A-Fa-f]{40}$") &&
                String.Equals(path,Path.Combine(Probe.IcueDir,"CorsairLLAccess64.sys"),StringComparison.OrdinalIgnoreCase);
        }
        internal static void ReadDcs(Snapshot snapshot,ServiceController[] services) {
            snapshot.DcsStart=-1;
            foreach(var service in services) {
                if(!service.ServiceName.Equals("CorsairDeviceControlService",StringComparison.OrdinalIgnoreCase)) continue;
                snapshot.DcsState=State(service.Status);
                using(var key=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\"+service.ServiceName))
                    snapshot.DcsStart=key==null ? -1 : Convert.ToInt32(key.GetValue("Start",-1));
                break;
            }
        }
        internal static void Read(Snapshot snapshot) {
            ServiceController[] services=ServiceController.GetServices();
            try { ReadDcs(snapshot,services); }
            finally { foreach(var service in services) service.Dispose(); }

            ServiceController[] drivers=ServiceController.GetDevices();
            try {
                foreach(var driver in drivers) {
                    string name=driver.ServiceName;
                    if(!Regex.IsMatch(name,"^CorsairLLAccess[0-9A-Fa-f]{40}$")) continue;
                    using(var key=Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\"+name)) {
                        if(key==null) throw new InvalidOperationException("驱动配置在检测期间消失，请重新检测。");
                        string path=Probe.NormalizeDriverPath(Probe.Text(key.GetValue("ImagePath")));
                        if(!IsIcueDriver(name,path)) continue;
                        snapshot.IcueDrivers.Add(new DriverInfo { Name=name,Path=path,State=State(driver.Status),Start=Convert.ToInt32(key.GetValue("Start",-1)) });
                    }
                }
            } finally { foreach(var driver in drivers) driver.Dispose(); }
        }
    }
}
