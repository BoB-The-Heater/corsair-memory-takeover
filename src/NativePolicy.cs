using System;
using System.Collections.Generic;
using System.Linq;

namespace CorsairTakeover {
    // Availability is observable; RGB ownership is a separate user-observed fact.
    public static class NativePolicy {
        public static bool Supports(string brand) { return brand=="ASUS" || brand=="MSI"; }
        public static string RuleName(string brand) { return brand+"-native-service-baseline-v1"; }
        public static string Product(string brand) { return brand=="ASUS" ? "Armoury Crate / Aura Sync" : "MSI Center / Mystic Light"; }
        public static void Evaluate(Snapshot s) {
            s.RuleEligible=false;
            if(!s.Ram.Any(Rules.IsCorsair)) s.RuleReason="未识别海盗船内存。";
            else if(s.Errors.Count>0 || String.IsNullOrEmpty(s.Fingerprint)) s.RuleReason="组件检测信息不完整。请导出报告查看缺失项。";
            else if(s.Requirements.Count==0 || s.Requirements.Any(r=>!r.Present)) s.RuleReason="官方接入组件未齐全；请先完成“品牌接入”中的步骤。";
            else if(!s.NativeServices.Any(v=>v.Role=="Lighting")) s.RuleReason="未找到可验证的官方灯效服务。此安装布局尚未适配。";
            else if(s.NativeServices.Any(v=>!v.Trusted || v.StartMode!="Auto")) s.RuleReason="目标服务签名未通过，或原启动方式不是自动；请先在官方软件修复安装。";
            else if(s.NativeServices.Any(v=>v.State!="Running" && v.State!="Stopped")) s.RuleReason="目标服务正在切换状态或暂停，稍后重新检测。";
            else { s.RuleEligible=true; s.RuleReason="可维护本机已配置的官方自动服务；首次启用需确认官方软件已经能控制内存。适配尚待对应主板实测。"; }
            if(!s.RuleEligible) { s.Health="接入条件待补齐"; s.HealthDetail=s.RuleReason; }
            else if(s.NativeServices.Any(v=>v.State=="Stopped")) { s.Health="官方自动服务未运行"; s.HealthDetail="仅恢复已确认基线中的停止服务，不重启正在运行的服务。服务可用不代表内存已同步。"; }
            else { s.Health="官方服务可用 · 接管待核实"; s.HealthDetail=s.NativeConfirmed ? "已保存用户确认的接管基线；当前灯效、重启与唤醒结果仍需在官方软件及实体内存上核实。" : "依赖检查通过。请先在官方软件完成一次实际内存控制，再启用持久维护。"; }
        }
        public static List<string> ToStart(Snapshot s,Enrollment e) {
            var result=new List<string>();
            if(e==null || e.Brand!=s.Brand || e.Fingerprint!=s.Fingerprint || String.IsNullOrEmpty(e.NativeConfirmedUtc) || !s.RuleEligible) return result;
            foreach(var v in s.NativeServices) if(e.BaselineServices.Contains(v.Name) && v.Trusted && v.StartMode=="Auto" && v.State=="Stopped") result.Add(v.Name);
            return result;
        }
        public static bool CanEnroll(Snapshot s) { return s.RuleEligible && s.NativeServices.Count>0 && s.NativeServices.All(v=>v.State=="Running"); }
        public static string Plan(Snapshot s) {
            return "适配："+Product(s.Brand)+"（预览版，待对应主板验证）\n\n"+
                "1. 检查官方软件、海盗船依赖和厂商签名。\n"+
                "2. 在官方软件中完成内存接管及一次实体同步确认。\n"+
                "3. 记录此时正在运行的官方自动服务、组件版本和文件指纹。\n"+
                "4. 安装本用户登录任务；登录和唤醒后，仅启动基线中已停止的自动服务。\n"+
                "5. 每次检查最多一次恢复，间隔至少 10 分钟，每个 UTC 日期最多 3 次。\n\n"+
                "保留 iCUE、设备服务的原有启动设置及官方授权。所有运行中的服务保持运行。\n"+
                "插件缺失、授权未完成、服务已运行但内存仍不可见，需要按官方流程处理。\n"+
                "组件或服务配置变化后暂停自动恢复；重新完成官方验证并撤销/启用才能建立新基线。\n"+
                "撤销会删除本工具任务及基线授权，保留原软件配置和运行状态。\n\n"+
                "本机检查："+s.RuleReason+"\n服务："+String.Join("、",s.NativeServices.Select(v=>v.Name+" ["+v.State+", "+v.StartMode+"]"));
        }
    }
}
