using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CorsairTakeover;

public static class UiTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static int count;
    static void Assert(bool condition,string message) { if(!condition) throw new Exception(message); count++; Console.WriteLine("PASS: "+message); }
    static T Field<T>(MainForm form,string name) { return (T)typeof(MainForm).GetField(name,Private).GetValue(form); }
    static void Invoke(MainForm form,string name,params object[] args) { typeof(MainForm).GetMethod(name,Private).Invoke(form,args); }
    static void Render(MainForm form,Snapshot snapshot) { typeof(MainForm).GetField("current",Private).SetValue(form,snapshot); Invoke(form,"Render"); Invoke(form,"SetBusy",false); }
    [STAThread] public static int Main() {
        string settingsDirectory=Path.Combine(Path.GetTempPath(),"CorsairTakeoverUiTests-"+Guid.NewGuid().ToString("N"));
        string settingsPath=Path.Combine(settingsDirectory,"preferences.json");
        try {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            using(var form=new MainForm(new UiPreferencesStore(settingsPath))) {
                // The detached control tree stays in the invisible WinForms parking window.
                // No Form.Show, message loop, keyboard/mouse input or service operation.
                Control root=form.Controls[0]; root.Font=form.Font; root.BackColor=form.BackColor; form.Controls.Remove(root);
                using(root) {
                    root.Dock=DockStyle.None; root.Size=new Size(1160,800); root.CreateControl();
                    var s=new Snapshot {Brand="GIGABYTE",BoardModel="Synthetic board",GccVersion="fixture",DcsVersion="fixture",DcsState="Running",GccPid=42,Fingerprint="fixture",BootUtc="2000-01-01T01:00:00Z",GccStartUtc="2000-01-01T01:01:00Z",Evidence=new LogEvidence {CurrentSession=true,Connected=true,Modules=2,SessionUtc="2000-01-01T01:01:00Z"}};
                    s.Ram.Add(new RamInfo {Vendor="Corsair",Bytes=16L*1024*1024*1024}); s.Ram.Add(new RamInfo {Vendor="Corsair",Bytes=16L*1024*1024*1024}); Rules.Evaluate(s); Render(form,s);
                    Assert(Field<Label>(form,"health").Text=="连接正常","matching current connection uses the selected overview");
                    Assert(!Field<Button>(form,"repair").Enabled,"an unadapted GIGABYTE configuration cannot expose hardware recovery");
                    Assert(form.ShouldExitAfterConnection("--repair",true),"a successful requested connection with complete current evidence may exit");
                    Assert(!form.ShouldExitAfterConnection("--repair",false),"a failed operation never exits on an otherwise healthy snapshot");
                    Assert(!form.ShouldExitAfterConnection("--scan",true)&&!form.ShouldExitAfterConnection("--enable",true),"scan and enrollment cannot trigger connection auto-exit");
                    s.Evidence.Connected=false; Assert(!form.ShouldExitAfterConnection("--repair",true),"process success without connection evidence cannot exit"); s.Evidence.Connected=true;
                    var nav=Field<Button[]>(form,"nav"); nav[1].PerformClick(); Assert(Field<Panel>(form,"detailsPage").Visible,"details navigation works");
                    nav[2].PerformClick(); Assert(Field<Panel>(form,"helpPage").Visible,"help navigation works");
                    var brands=Field<ComboBox>(form,"brands"); brands.SelectedIndex=1;
                    Assert(Field<RichTextBox>(form,"guidance").Text.Contains("华硕"),"brand selection updates actual help text");
                    Assert(Field<Label>(form,"helpContext").Text.Contains("其他品牌"),"viewing another brand does not change detected hardware"); nav[0].PerformClick();
                    s.Errors.Add("synthetic access failure"); Render(form,s);
                    Assert(!form.ShouldExitAfterConnection("--repair",true),"partial scan cannot trigger silent exit");
                    Assert(Field<Label>(form,"health").Text=="检测信息还不完整","read failure cannot keep a green success headline");
                    Assert(!Field<Label[]>(form,"stepText")[1].Text.Contains("已确认"),"partial scan removes connection confirmation");
                    Assert(!Field<Button>(form,"repair").Enabled&&Field<Button>(form,"primary").Text=="重新检测","partial scan offers retry and blocks recovery");
                    Render(form,null); Assert(!Field<Label[]>(form,"stepText")[0].Text.Contains("已识别"),"failed scan cannot retain completed steps from an earlier snapshot");
                    s=new Snapshot {Brand="ASUS",RuleEligible=true,NativeConfirmed=true,AssistantEnabled=true,AssistantState="已开启",Health="官方服务已就绪"};
                    s.NativeServices.Add(new NativeService {Name="fixture",State="Stopped",Role="Lighting",Trusted=true,StartMode="Auto"}); Render(form,s);
                    Assert(Field<Button>(form,"primary").Text=="恢复基线服务","confirmed stopped native service offers its existing action");
                    s.AssistantState="版本变化，自动恢复暂停"; Render(form,s);
                    Assert(Field<Label>(form,"health").Text.Contains("暂停")&&Field<UiState>(form,"presentation").PrimaryAction=="details","environment drift leads to inspection");
                    Assert(!form.ShouldExitAfterConnection("--repair",true),"native service readiness is not proof of a GIGABYTE connection");
                    nav[3].PerformClick(); Assert(Field<Panel>(form,"settingsPage").Visible,"settings navigation works");
                    var loginSample=new Snapshot { Brand="GIGABYTE",BoardModel="B550 VISION D-P",OsVersion="10.0.28000.1",Fingerprint=new string('1',64),GccVersion="26.03.26.01",StorageVersion="26.04.27.02",DcsVersion="1.9.15",IcueVersion="5.46.67",DcsStart=2,DcsState="Running",GccPath=Path.Combine(Probe.GccDir,"GCC.exe") };
                    loginSample.Ram.Add(new RamInfo {Vendor="Corsair",Part="CMT32GX4M2D3600C18"}); loginSample.Ram.Add(new RamInfo {Vendor="Corsair",Part="CMT32GX4M2D3600C18"});
                    loginSample.IcueDrivers.Add(new DriverInfo {Name="CorsairLLAccess"+new string('A',40),Path=Path.Combine(Probe.IcueDir,"CorsairLLAccess64.sys"),Start=3,State="Stopped"}); Render(form,loginSample);
                    Invoke(form,"RenderLoginStatus",new StartupStatus { Message="登录自动连接未开启。" },null);
                    Assert(Field<Button>(form,"enableLogin").Enabled&&!Field<Button>(form,"disableLogin").Enabled,"supported disabled startup may be enabled");
                    Invoke(form,"RenderLoginStatus",null,"synthetic read failure");
                    Assert(!Field<Button>(form,"enableLogin").Enabled&&!Field<Button>(form,"disableLogin").Enabled&&Field<Label>(form,"loginStatus").Text.Contains("读取失败"),"unknown startup status never becomes a disabled success state");
                    Invoke(form,"RenderLoginStatus",new StartupStatus {Enabled=true,CanDisable=true,Message="登录自动连接已开启。",LastResult=new OperationResult {Success=false,Message="synthetic connection failure"}},null);
                    Assert(!Field<Button>(form,"enableLogin").Enabled&&Field<Button>(form,"disableLogin").Enabled&&Field<Label>(form,"persistence").Text.Contains("已开启"),"enabled task readback updates settings and overview");
                    Assert(Field<Label>(form,"loginResult").Text.Contains("未完成"),"last startup failure stays visible even when enrollment remains enabled");
                    Invoke(form,"SetBusy",true);
                    Assert(!Field<Button>(form,"enableLogin").Enabled&&!Field<Button>(form,"disableLogin").Enabled,"in-flight operation disables both startup mutations");
                    Invoke(form,"SetBusy",false); Render(form,null);
                    Invoke(form,"RenderLoginStatus",new StartupStatus {CanDisable=true,Message="授权未启用，但仍有本工具登录任务，请关闭以清理。"},null);
                    Assert(!Field<Button>(form,"enableLogin").Enabled&&Field<Button>(form,"disableLogin").Enabled,"owned partial enrollment can be disabled even after a scan failure");
                    Render(form,new Snapshot {Brand="ASUS"}); Invoke(form,"RenderLoginStatus",new StartupStatus {Message="登录自动连接未开启。"},null);
                    Assert(!Field<Button>(form,"enableLogin").Enabled,"GIGABYTE startup cannot be enabled on a different brand");
                    Assert(!form.ShouldExitAfterConnection("--startup-enable-confirmed "+new string('1',64),true),"enabling startup never triggers manual connection auto-exit");
                    Field<CheckBox>(form,"hideTaskbar").Checked=true; Field<CheckBox>(form,"exitAfterConnect").Checked=false; Invoke(form,"SavePreferences");
                    var saved=new UiPreferencesStore(settingsPath).Load();
                    Assert(saved.HideTaskbar&&!saved.ExitAfterConnect&&!form.ShowInTaskbar,"settings save applies both choices and taskbar visibility");
                    Assert(Field<NotifyIcon>(form,"tray")==null,"offscreen verification never creates a desktop tray entry");
                    Field<CheckBox>(form,"hideTaskbar").Checked=false; Invoke(form,"SavePreferences");
                    Assert(form.ShowInTaskbar,"turning off taskbar hiding restores the taskbar property");
                    File.WriteAllText(settingsPath,"{invalid");
                    using(var invalid=new MainForm(new UiPreferencesStore(settingsPath))) {
                        Assert(invalid.ShowInTaskbar&&!Field<CheckBox>(invalid,"exitAfterConnect").Checked,"invalid settings retain access and disable silent exit");
                        Assert(Field<Label>(invalid,"settingsStatus").Text.Contains("读取失败"),"corrupt settings report an explicit error");
                    }
                    nav[0].PerformClick();
                    Invoke(form,"SetBusy",true);
                    Assert(!Field<Button>(form,"scan").Enabled&&!Field<Button>(form,"primary").Enabled&&!Field<Button>(form,"undo").Enabled,"operation in progress blocks duplicate actions");
                    root.Size=new Size(960,680); root.PerformLayout();
                    Assert(Field<Button>(form,"scan").Width>0&&Field<Button>(form,"primary").Width>0,"small window retains primary controls");
                    Assert(!form.Visible&&Application.OpenForms.Count==0,"verification never opens an application window");
                    form.Dispose(); Invoke(form,"SetBusy",false);
                }
            }
            Console.WriteLine("Passed "+count+" UI checks."); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; }
        finally { if(File.Exists(settingsPath)) File.Delete(settingsPath); if(Directory.Exists(settingsDirectory)) Directory.Delete(settingsDirectory); }
    }
}
