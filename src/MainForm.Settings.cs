using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CorsairTakeover {
    public sealed partial class MainForm {
        readonly UiPreferencesStore preferencesStore;
        UiPreferences preferences;
        string preferencesError;
        Panel settingsPage;
        CheckBox hideTaskbar,exitAfterConnect;
        Label settingsStatus;
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        Icon applicationIcon;
        Bitmap identityImage;
        StartupStatus loginState;
        Label loginStatus,loginResult;
        Button enableLogin,disableLogin;

        Panel BuildSettings() {
            var page=new Panel { Padding=new Padding(52,38,52,24),AutoScroll=true };
            var layout=new TableLayoutPanel { Dock=DockStyle.Top,Height=722,ColumnCount=1,RowCount=14,Margin=Padding.Empty };
            foreach(int height in new[]{50,54,54,50,56,18,52,44,44,44,66,52,46,42}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.Controls.Add(Label("开机自动连接",22,Ink,FontStyle.Bold),0,0);
            layout.Controls.Add(Label("登录 Windows 后自动连接技嘉内存，确认成功即退出。\n每次登录只执行一次，不在后台常驻；首次开启需要管理员授权。",10.5f,Muted),0,1);
            var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty };
            enableLogin=MakeButton("开启自动连接",true); enableLogin.Width=180; enableLogin.Enabled=false;
            disableLogin=MakeButton("关闭自动连接",false); disableLogin.Width=180; disableLogin.Enabled=false;
            enableLogin.Click+=async delegate {
                if(busy||current==null) return;
                if(MessageBox.Show(this,"开启后，每次登录 Windows 会在后台尝试一次连接，必要时重开 GCC 和设备服务。确认成功后退出，失败会记录结果。\n\n本次只保存配置，不立即重启设备。是否开启？","开启登录自动连接",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)==DialogResult.Yes)
                    await Action("--startup-enable-confirmed "+current.Fingerprint);
            };
            disableLogin.Click+=async delegate { await Action("--startup-disable"); };
            actions.Controls.Add(enableLogin); actions.Controls.Add(disableLogin); layout.Controls.Add(actions,0,2);
            loginStatus=Label("尚未读取自动连接状态。",10.5f,Muted); layout.Controls.Add(loginStatus,0,3);
            loginResult=Label("",10.5f,Muted); layout.Controls.Add(loginResult,0,4);
            layout.Controls.Add(Divider(DockStyle.Top),0,5);
            layout.Controls.Add(Label("窗口与退出",20,Ink,FontStyle.Bold),0,6);
            hideTaskbar=new CheckBox { Text="隐藏任务栏入口",Dock=DockStyle.Fill,Checked=preferences.HideTaskbar,AutoSize=false,Margin=Padding.Empty };
            layout.Controls.Add(hideTaskbar,0,7);
            layout.Controls.Add(Label("隐藏后保留系统托盘图标，双击可打开窗口，右键可退出。",10.5f,Muted),0,8);
            exitAfterConnect=new CheckBox { Text="连接成功后静默退出",Dock=DockStyle.Fill,Checked=preferences.ExitAfterConnect,AutoSize=false,Margin=Padding.Empty };
            layout.Controls.Add(exitAfterConnect,0,9);
            layout.Controls.Add(Label("手动连接成功后关闭助手，不弹成功提示；失败时保留窗口。\n此选项不影响登录自动连接，登录任务执行完毕始终退出。",10.5f,Muted),0,10);
            var save=MakeButton("保存窗口设置",true); save.Width=170; save.Dock=DockStyle.Left; save.Click+=delegate { SavePreferences(); }; layout.Controls.Add(save,0,11);
            settingsStatus=Label(preferencesError??"窗口选项保存在当前 Windows 用户下。",10.5f,preferencesError==null?Muted:Color.FromArgb(153,98,21)); layout.Controls.Add(settingsStatus,0,12);
            layout.Controls.Add(Label("关闭窗口不取消自动连接。停止开机连接请点击上方“关闭自动连接”。",10.5f,Muted),0,13);
            page.Controls.Add(layout); return page;
        }
        async Task RefreshLoginStatus() {
            try { var status=await Task.Run(()=>StartupConnection.ReadStatus()); if(!IsDisposed&&!Disposing) RenderLoginStatus(status,null); }
            catch(Exception ex) { if(!IsDisposed&&!Disposing) RenderLoginStatus(null,ex.Message); }
        }
        void RenderLoginStatus(StartupStatus status,string error) {
            loginState=status;
            loginStatus.Text=error!=null?"自动连接状态读取失败："+error:status==null?"尚未读取自动连接状态。":status.Message;
            loginStatus.ForeColor=error!=null?Color.FromArgb(153,98,21):status!=null&&status.Enabled?Green:Muted;
            var result=status==null?null:status.LastResult;
            loginResult.Text=result==null?"尚无登录连接结果。":(result.Success?"上次连接成功：":"上次连接未完成：")+result.Message;
            loginResult.ForeColor=result!=null&&!result.Success?Color.FromArgb(153,98,21):Muted;
            UpdateLoginButtons();
            if(current!=null&&current.Brand=="GIGABYTE") {
                persistence.Text=status!=null&&status.Enabled?"登录自动连接已开启":"登录自动连接尚未确认开启";
                maintenanceBody.Text=error!=null?"自动连接状态读取失败，请到设置页查看。":status!=null&&status.Enabled?"登录后连接一次，完成后退出。可在设置中关闭。":"到设置页开启登录自动连接，成功后退出。";
            }
        }
        void UpdateLoginButtons() {
            if(enableLogin==null) return;
            enableLogin.Enabled=!busy&&loginState!=null&&!loginState.Enabled&&!loginState.CanDisable&&GigabyteRecovery.CanConnect(current);
            disableLogin.Enabled=!busy&&loginState!=null&&loginState.CanDisable;
        }
        void SavePreferences() {
            var next=new UiPreferences { HideTaskbar=hideTaskbar.Checked,ExitAfterConnect=exitAfterConnect.Checked };
            bool saved=false;
            try {
                preferencesStore.Save(next); saved=true; preferences=next; preferencesError=null;
                UpdateTray(); ShowInTaskbar=!preferences.HideTaskbar;
                settingsStatus.ForeColor=Green; settingsStatus.Text="设置已保存，下次打开仍然生效。";
            } catch(Exception ex) { settingsStatus.ForeColor=Color.FromArgb(153,98,21); settingsStatus.Text=(saved?"设置已保存，但窗口更新失败：":"设置保存失败：")+ex.Message; }
        }
        void UpdateTray() {
            // An invisible/offscreen form must never create a desktop tray entry.
            if(!Visible) return;
            if(!preferences.HideTaskbar) { if(tray!=null) tray.Visible=false; return; }
            if(tray==null) {
                trayMenu=new ContextMenuStrip();
                trayMenu.Items.Add("打开接管助手",null,delegate { WindowState=FormWindowState.Normal; Show(); Activate(); });
                trayMenu.Items.Add("退出",null,delegate { Close(); });
                tray=new NotifyIcon { Icon=applicationIcon,Text="Corsair 内存接管助手",ContextMenuStrip=trayMenu };
                tray.DoubleClick+=delegate { WindowState=FormWindowState.Normal; Show(); Activate(); };
            }
            tray.Visible=true;
        }
        internal bool ShouldExitAfterConnection(string command,bool succeeded) {
            return command=="--repair" && succeeded && preferences.ExitAfterConnect && GigabyteRecovery.IsConnected(current);
        }
    }
}
