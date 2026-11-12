using System;
using System.Drawing;
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

        Panel BuildSettings() {
            var page=new Panel { Padding=new Padding(52,38,52,24),AutoScroll=true };
            var layout=new TableLayoutPanel { Dock=DockStyle.Top,Height=440,ColumnCount=1,RowCount=8,Margin=Padding.Empty };
            foreach(int height in new[]{52,44,52,44,72,58,62,56}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.Controls.Add(Label("窗口与退出",22,Ink,FontStyle.Bold),0,0);
            hideTaskbar=new CheckBox { Text="隐藏任务栏入口",Dock=DockStyle.Fill,Checked=preferences.HideTaskbar,AutoSize=false,Margin=Padding.Empty };
            layout.Controls.Add(hideTaskbar,0,1);
            layout.Controls.Add(Label("隐藏后保留系统托盘图标，双击可打开窗口，右键可退出。",10.5f,Muted),0,2);
            exitAfterConnect=new CheckBox { Text="连接成功后静默退出",Dock=DockStyle.Fill,Checked=preferences.ExitAfterConnect,AutoSize=false,Margin=Padding.Empty };
            layout.Controls.Add(exitAfterConnect,0,3);
            layout.Controls.Add(Label("主动连接技嘉内存后，确认本次连接日志正常便关闭助手，不弹成功提示。\n连接失败或未确认时保留窗口；普通检测不会自动退出。",10.5f,Muted),0,4);
            var save=MakeButton("保存设置",true); save.Width=156; save.Dock=DockStyle.Left; save.Click+=delegate { SavePreferences(); }; layout.Controls.Add(save,0,5);
            settingsStatus=Label(preferencesError??"设置保存在当前 Windows 用户下。",10.5f,preferencesError==null?Muted:Color.FromArgb(153,98,21)); layout.Controls.Add(settingsStatus,0,6);
            layout.Controls.Add(Label("空闲时不循环检测。关闭助手不会关闭官方软件；已启用的登录与唤醒维护仍按原设置运行。",10.5f,Muted),0,7);
            page.Controls.Add(layout); return page;
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
