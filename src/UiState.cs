using System;
using System.Linq;

namespace CorsairTakeover {
    // Presentation only: actionable commands remain guarded by Operations.
    public sealed class UiState {
        public string Title, Detail, Tone, Evidence, NextTitle, NextBody, PrimaryText, PrimaryAction;
        public static UiState For(Snapshot s) {
            var v=new UiState{Title="正在检查接管环境",Detail="读取本机硬件与官方软件状态。",Tone="neutral",Evidence="正在检测",NextTitle="检查完成后显示下一步",NextBody="首次检查只读取信息。",PrimaryText="正在检测…",PrimaryAction="none"};
            if(s==null) return v;
            v.Title=s.Health; v.Detail=s.HealthDetail; v.Evidence="实体同步待验证";
            v.PrimaryText="查看检测详情"; v.PrimaryAction="details";
            v.NextTitle="先确认当前接管状态"; v.NextBody="在官方软件中查看内存是否出现，并确认实体灯效跟随。";
            if(s.Errors.Count>0) { v.Title="检测信息还不完整"; v.Tone="warning"; v.Detail="部分信息读取失败，可在检测详情中查看原因。"; v.NextTitle="先补齐检测信息"; v.NextBody="这次检测没有确认完整的接管环境。重新检测后，再进行维护操作。"; v.PrimaryText="重新检测"; v.PrimaryAction="scan"; return v; }
            if(s.AssistantEnabled && (s.AssistantState??"").Contains("暂停")) { v.Title="环境有变化，维护已暂停"; v.Tone="warning"; v.NextTitle="重新核实官方接管"; v.NextBody="软件或硬件发生变化。查看检测详情，完成官方接管测试，再撤销旧设置并重新启用。"; return v; }
            if(!NativePolicy.Supports(s.Brand)) {
                v.Title=s.Health; v.Detail=s.HealthDetail; v.Tone=s.Health=="接管链路正常"?"success":"neutral";
                v.Evidence="当前仅检测"; v.NextTitle="在官方软件中完成接入";
                v.NextBody="此品牌提供检测与接入指引。请在官方软件中检查内存识别和灯效。";
                v.PrimaryText="查看接入步骤"; v.PrimaryAction="help"; return v;
            }
            if(!s.RuleEligible) { v.Title="还需要完成接入检查"; v.Detail=s.RuleReason; v.Tone="warning"; v.Evidence="当前仅检测"; v.NextTitle="按品牌补齐接入条件"; v.NextBody="查看对应品牌的安装和接管步骤。未知组件或不支持的组合不会开放自动维护。"; v.PrimaryText="查看接入步骤"; v.PrimaryAction="help"; return v; }
            bool native=NativePolicy.Supports(s.Brand);
            if(native) {
                bool stopped=s.NativeServices.Any(x=>x.State=="Stopped");
                v.Title=stopped?"有官方自动服务尚未运行":"官方服务已就绪";
                v.Tone=stopped?"warning":"neutral"; v.Evidence="服务状态已确认";
                v.Detail="服务可用与内存实际同步是两项检查。请在官方软件中核实内存接管。";
                if(stopped && s.NativeConfirmed) { v.NextTitle="恢复已确认的基线服务"; v.NextBody="仅启动基线中已经停止的自动服务，保留正在运行的服务和灯效配置。"; v.PrimaryText="恢复基线服务"; v.PrimaryAction="repair"; }
                else if(!NativePolicy.CanEnroll(s)) { v.NextTitle="先让官方软件正常接管"; v.NextBody="官方服务尚未全部运行。先按接入说明完成配置，实际同步成功后再保存维护基线。"; v.PrimaryText="查看接入步骤"; v.PrimaryAction="help"; }
                else if(!s.AssistantEnabled) { v.NextTitle="确认同步后，启用持久维护"; v.NextBody="先在官方软件中确认内存可见、灯效跟随。启用时会保存这次确认和当前服务基线。"; v.PrimaryText="启用持久维护"; v.PrimaryAction="enable"; }
                else { v.NextTitle="验证一次重启后的同步"; v.NextBody="持久维护已经开启。保存工作后自行重启，再检查官方软件和实体内存；本工具不会重启电脑。"; v.PrimaryText="查看验收步骤"; v.PrimaryAction="help"; }
            }
            return v;
        }
    }
}
