namespace CorsairTakeover {
    public static class Guidance {
        public static string For(string brand) {
            if(brand=="GIGABYTE") return "技嘉 · GCC / RGB Fusion\n\n1. 在 GCC 中安装 RGB Fusion 和所需内存组件。\n2. 查看检测详情中的当前连接状态。\n3. 按官方说明完成内存识别与同步设置。\n\n此品牌当前仅提供检测与接入指引。\n\n官方排查说明：\nhttps://help.corsair.com/hc/en-us/articles/4424077074189";
            if(brand=="ASUS") return "华硕 · Armoury Crate / Aura Sync\n\n1. 安装官方 Corsair RGB Memory Plugin for ASUS AURA SYNC 和 Armoury Crate。官方也提供不安装完整 iCUE 的插件独立路径。\n2. 若安装 iCUE，在内存设置中将 Device Control Service 打开，或批准 Armoury Crate 的控制请求。不要撤回 Revert Control。\n3. 在 Armoury Crate 的 Device → 主板 → Addressable Headers 中 Rescan，再到 Aura Sync 确认 Memory 并测试实体灯效。界面可能随版本变化。\n4. 回到本工具重新检测，点击“启用持久维护”，记录已经成功的接管基线。\n5. 重启及唤醒后复核。工具只恢复基线中的停止自动服务；不会伪造官方控制授权。\n\n卸载 iCUE 后，官方要求重新安装内存插件，即使安装列表中仍有插件。\n\n官方步骤：\nhttps://help.corsair.com/hc/en-us/articles/31086864483729\nhttps://help.corsair.com/hc/en-us/articles/360035656371";
            if(brand=="MSI") return "微星 · MSI Center / Mystic Light\n\n1. 按官方流程安装兼容版本的 iCUE、MSI Center。\n2. 在 MSI Center 的 Feature Sets 中安装 Mystic Light，按提示重启，通过 Support / Live Update 检查核心组件。\n3. 在 Mystic Light 中确认内存出现，加入同步并测试实体灯效。\n4. 本工具重新检测通过后，点击“启用持久维护”，保存已成功的服务基线。\n5. 重启及唤醒后核实内存仍然可控。\n\n保留 iCUE 及现有官方授权。工具只启动基线中停止的自动服务，不重启 MSI Center 全套服务，也不停止其他灯效软件。\n\n旧版 Dragon Center、MSI Center S、未知插件布局不在当前规则内。\n\n官方步骤：\nhttps://help.corsair.com/hc/en-us/articles/14649496854029\nhttps://www.msi.com/support/technical_details/MB_SW_MSI_Center";
            if(brand=="ASRock") return "华擎 · Polychrome RGB\n\n需要按具体主板、内存型号与 Polychrome 版本核实兼容性。当前没有经过实机验证的自动接管规则。\n\n请先检查主板官方支持页及内存兼容信息。检测报告可以记录环境，但不能使官方不支持的设备自动获得兼容性。\n\n官方产品页：\nhttps://www.asrock.com/microsite/PolyChromeRGB/";
            return "其他品牌\n\n当前提供硬件及已安装 RGB 软件的检测。尚未验证此品牌的接管路径，因此不会自动修改服务或启动项。需要先确定厂商官方软件是否支持该内存型号。";
        }
    }
}
