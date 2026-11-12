using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CorsairTakeover {
    public sealed partial class MainForm : Form {
        static readonly Color Ink=Color.FromArgb(30,39,51),Muted=Color.FromArgb(88,97,111),Accent=Color.FromArgb(18,101,212),Canvas=Color.FromArgb(245,245,246),Green=Color.FromArgb(34,112,62),Line=Color.FromArgb(220,223,227);
        Panel pageHost,overview,detailsPage,helpPage; TableLayoutPanel overviewLayout;
        Label health,healthDetail,statusIcon,hardware,software,hardwareNote,softwareNote,nextTitle,nextBody,persistence,maintenanceBody,footer,helpContext,selectionText,observed;
        Label[] stepText,stepIcon;
        Button[] nav; Button scan,primary,planButton,undo,export,repair;
        DataGridView checks; RichTextBox guidance; ComboBox brands;
        Snapshot current; UiState presentation; bool busy; string lastOperation;
        readonly ToolTip tips=new ToolTip { AutoPopDelay=18000,InitialDelay=400,ReshowDelay=150 };

        public MainForm() : this(new UiPreferencesStore()) { }
        internal MainForm(UiPreferencesStore store) {
            preferencesStore=store;
            try { preferences=store.Load(); }
            catch(Exception ex) { preferences=new UiPreferences { ExitAfterConnect=false }; preferencesError="设置读取失败："+ex.Message; }
            SuspendLayout(); AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            Text="Corsair 内存接管助手"; Font=new Font("Microsoft YaHei UI",11); ClientSize=new Size(1160,800); MinimumSize=new Size(960,680);
            StartPosition=FormStartPosition.CenterScreen; ForeColor=Ink; BackColor=Color.White; applicationIcon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); Icon=applicationIcon; KeyPreview=true; ShowInTaskbar=!preferences.HideTaskbar;
            var root=Grid(1,3); root.RowStyles.Add(new RowStyle(SizeType.Absolute,64)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); Controls.Add(root);
            var header=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(28,10,28,10) };
            var identity=MakeButton("接管助手",false); identity.Font=new Font(Font.FontFamily,20,FontStyle.Bold); identity.ForeColor=Ink; identity.Width=218; identity.Dock=DockStyle.Left; identity.Click+=delegate { Navigate(0); }; identity.FlatAppearance.BorderSize=0; identityImage=applicationIcon.ToBitmap(); identity.Image=identityImage; identity.TextImageRelation=TextImageRelation.ImageBeforeText; header.Controls.Add(identity);
            var links=new FlowLayoutPanel { Dock=DockStyle.Right,Width=390,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,Padding=new Padding(0,2,0,0) };
            nav=new Button[4]; string[] names={"概览","检测详情","帮助","设置"};
            for(int i=0;i<4;i++) { int index=i; nav[i]=MakeButton(names[i],false); nav[i].Width=i==1?108:76; nav[i].Height=38; nav[i].FlatAppearance.BorderSize=0; nav[i].Click+=delegate { Navigate(index); }; links.Controls.Add(nav[i]); }
            header.Controls.Add(links); header.Controls.Add(Divider(DockStyle.Bottom)); root.Controls.Add(header,0,0);
            pageHost=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty }; root.Controls.Add(pageHost,0,1);
            footer=Label("F5 重新检测 · 数据保存在本机",9.5f,Muted); footer.Padding=new Padding(32,0,28,0); footer.TextAlign=ContentAlignment.MiddleLeft; root.Controls.Add(footer,0,2);
            overview=BuildOverview(); detailsPage=BuildDetails(); helpPage=BuildHelp(); settingsPage=BuildSettings();
            detailsPage.Padding=helpPage.Padding=new Padding(32,24,32,16);
            foreach(var page in new[]{overview,detailsPage,helpPage,settingsPage}) { page.Dock=DockStyle.Fill; pageHost.Controls.Add(page); }
            Navigate(0); ResumeLayout(true); SetBusy(false);
            Shown+=async delegate { UpdateTray(); if(preferencesError!=null) Navigate(3); var area=Screen.FromControl(this).WorkingArea; if(Width>area.Width-32||Height>area.Height-32) Size=new Size(Math.Min(Width,area.Width-32),Math.Min(Height,area.Height-32)); await RefreshSnapshot(); };
            KeyDown+=async delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.F5&&!busy) { e.Handled=true; await RefreshSnapshot(); } else if(e.Alt&&e.KeyCode>=Keys.D1&&e.KeyCode<=Keys.D4) { Navigate((int)e.KeyCode-(int)Keys.D1); e.Handled=true; } };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy&&e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; footer.Text="操作仍在进行，完成后可退出。"; } };
        }
        static TableLayoutPanel Grid(int columns,int rows) { return new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=columns,RowCount=rows,Margin=Padding.Empty }; }
        static Label Label(string text,float size,Color color,FontStyle style=FontStyle.Regular) { return new Label { Text=text,Font=new Font("Microsoft YaHei UI",size,style),ForeColor=color,Dock=DockStyle.Fill,UseMnemonic=false,Margin=Padding.Empty,AutoEllipsis=true }; }
        static Panel Divider(DockStyle dock) { return new Panel { Dock=dock,Height=1,BackColor=Line,Margin=Padding.Empty }; }
        Button MakeButton(string text,bool filled) {
            var b=new Button { Text=text,Font=Font,Height=44,Width=154,FlatStyle=FlatStyle.Flat,BackColor=filled?Accent:Color.Transparent,ForeColor=filled?Color.White:Accent,Cursor=Cursors.Hand,UseVisualStyleBackColor=false,Margin=new Padding(0,0,12,0),AccessibleName=text };
            b.FlatAppearance.BorderColor=filled?Accent:Color.FromArgb(167,181,201); b.FlatAppearance.MouseOverBackColor=filled?Color.FromArgb(12,83,178):Color.FromArgb(231,239,249); return b;
        }
        Panel Card() { return new Panel { Dock=DockStyle.Fill,Padding=new Padding(18),Margin=Padding.Empty,BackColor=Canvas }; }
        static Label Glyph(string value,float size,Color color) { var label=Label(value,size,color); label.Font=new Font("Segoe MDL2 Assets",size); label.AutoEllipsis=false; label.TextAlign=ContentAlignment.MiddleLeft; return label; }
        Panel BuildOverview() {
            var page=new Panel { AutoScroll=true }; overviewLayout=Grid(2,1); overviewLayout.Dock=DockStyle.Top; overviewLayout.MinimumSize=new Size(0,640); overviewLayout.Height=640;
            overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60)); overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40)); page.Controls.Add(overviewLayout);
            page.ClientSizeChanged+=delegate { overviewLayout.Height=Math.Max(overviewLayout.MinimumSize.Height,page.ClientSize.Height); };
            var left=Grid(1,10); left.Padding=new Padding(52,42,48,20); overviewLayout.Controls.Add(left,0,0);
            float[] heights={34,76,68,18,224,28,52,32,12}; foreach(float h in heights) left.RowStyles.Add(new RowStyle(SizeType.Absolute,h)); left.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            left.Controls.Add(Label("接管状态",12,Muted),0,0);
            var status=Grid(2,1); status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,54)); status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            statusIcon=Glyph("\uE946",30,Muted); status.Controls.Add(statusIcon,0,0); health=Label("正在检查连接",30,Ink,FontStyle.Bold); health.TextAlign=ContentAlignment.MiddleLeft; status.Controls.Add(health,1,0); left.Controls.Add(status,0,1);
            healthDetail=Label("读取硬件、官方组件与当前会话。",13,Muted); healthDetail.Padding=new Padding(0,10,0,0); left.Controls.Add(healthDetail,0,2);
            var rows=Grid(1,4); for(int i=0;i<4;i++) rows.RowStyles.Add(new RowStyle(SizeType.Percent,25));
            hardware=DeviceRow(rows,0,"主板"); hardwareNote=DeviceRow(rows,1,"内存"); software=DeviceRow(rows,2,"控制软件"); softwareNote=DeviceRow(rows,3,"软件版本"); left.Controls.Add(rows,0,4);
            var actions=new FlowLayoutPanel { Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false }; scan=MakeButton("重新检测",true); scan.Font=new Font(Font.FontFamily,12); scan.Width=220; scan.Height=50; scan.Click+=async delegate { await RefreshSnapshot(); }; actions.Controls.Add(scan);
            var details=MakeButton("查看检测详情",false); details.FlatAppearance.BorderSize=0; details.Height=50; details.Click+=delegate { Navigate(1); }; actions.Controls.Add(details); left.Controls.Add(actions,0,6);
            observed=Label("等待首次检测",10,Muted); observed.Padding=new Padding(0,8,0,0); left.Controls.Add(observed,0,7);
            var right=Grid(1,12); right.BackColor=Canvas; right.Padding=new Padding(40,42,36,20); overviewLayout.Controls.Add(right,1,0);
            float[] rightHeights={30,72,10,50,50,58,76,48,20,27,52}; foreach(float h in rightHeights) right.RowStyles.Add(new RowStyle(SizeType.Absolute,h)); right.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            right.Controls.Add(Label("完成接入",11.5f,Muted),0,0); nextTitle=Label("正在检查接入条件",21,Ink,FontStyle.Bold); right.Controls.Add(nextTitle,0,1);
            stepText=new Label[3]; stepIcon=new Label[3]; string[] steps={"1  检查官方组件","2  确认当前连接","3  切换灯效并检查内存"};
            for(int i=0;i<3;i++) { var row=Grid(2,1); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,36)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); stepIcon[i]=Glyph("\uE946",18,Muted); row.Controls.Add(stepIcon[i],0,0); stepText[i]=Label(steps[i],14,Ink,FontStyle.Bold); stepText[i].TextAlign=ContentAlignment.MiddleLeft; row.Controls.Add(stepText[i],1,0); right.Controls.Add(row,0,i+3); }
            nextBody=Label("检查完成后显示下一步。",12.5f,Muted); right.Controls.Add(nextBody,0,6);
            primary=MakeButton("查看操作步骤",false); primary.Font=new Font(Font.FontFamily,12); primary.Width=250; primary.Click+=async delegate { await PrimaryAction(); }; right.Controls.Add(primary,0,7);
            var separator=Divider(DockStyle.Bottom); right.Controls.Add(separator,0,8);
            persistence=Label("检测与接入指引",12,Ink,FontStyle.Bold); right.Controls.Add(persistence,0,9);
            maintenanceBody=Label("根据本机品牌提供接入方式。",10.5f,Muted); right.Controls.Add(maintenanceBody,0,10);
            var maintenance=new FlowLayoutPanel { Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=true };
            planButton=MakeButton("执行方案",false); planButton.Width=110; planButton.Height=32; planButton.FlatAppearance.BorderSize=0; planButton.Click+=delegate { ShowPlan(); }; maintenance.Controls.Add(planButton);
            undo=MakeButton("撤销本工具设置",false); undo.Width=150; undo.Height=32; undo.FlatAppearance.BorderSize=0; undo.Click+=async delegate { await Action("--restore"); }; maintenance.Controls.Add(undo); right.Controls.Add(maintenance,0,11);
            return page;
        }
        Label DeviceRow(TableLayoutPanel parent,int index,string caption) {
            var panel=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty }; panel.Controls.Add(Divider(DockStyle.Top));
            var row=Grid(2,1); row.Padding=new Padding(0,10,0,6); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            var key=Label(caption,14,Muted); key.TextAlign=ContentAlignment.MiddleLeft; row.Controls.Add(key,0,0);
            var value=Label("读取中…",14,Ink,FontStyle.Bold); value.TextAlign=ContentAlignment.MiddleLeft; row.Controls.Add(value,1,0); panel.Controls.Add(row); row.BringToFront(); parent.Controls.Add(panel,0,index); return value;
        }
        Panel BuildDetails() {
            var p=new Panel(); var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty }; layout.RowStyles.Add(new RowStyle(SizeType.Absolute,51)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,115)); p.Controls.Add(layout);
            var bar=new FlowLayoutPanel { Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false }; export=MakeButton("导出检测报告",false); export.Width=152; export.Click+=delegate { Export(); }; repair=MakeButton("恢复当前连接",false); repair.Width=164; repair.Click+=async delegate { await Action("--repair"); }; bar.Controls.Add(export); bar.Controls.Add(repair); layout.Controls.Add(bar,0,0);
            checks=new DataGridView { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AllowUserToOrderColumns=false,AutoGenerateColumns=false,AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BorderStyle=BorderStyle.None,BackgroundColor=Color.White,GridColor=Color.FromArgb(228,234,241),CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None,EnableHeadersVisualStyles=false,ColumnHeadersHeight=42,ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing,AccessibleName="接管检测详情" };
            checks.DefaultCellStyle=new DataGridViewCellStyle { Font=new Font("Microsoft YaHei UI",10.5f),ForeColor=Ink,BackColor=Color.White,Padding=new Padding(12,10,12,10),WrapMode=DataGridViewTriState.True,SelectionBackColor=Color.FromArgb(230,240,253),SelectionForeColor=Ink };
            checks.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle { BackColor=Color.FromArgb(233,240,247),ForeColor=Muted,Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold),Padding=new Padding(12,0,0,0) };
            string[] columns={"检查项目","当前状态","说明"}; float[] weights={23,27,50}; for(int i=0;i<3;i++) checks.Columns.Add(new DataGridViewTextBoxColumn { Name="c"+i,HeaderText=columns[i],AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,FillWeight=weights[i],SortMode=DataGridViewColumnSortMode.NotSortable,MinimumWidth=i==2?230:145 });
            checks.SelectionChanged+=delegate { if(checks.CurrentRow!=null&&selectionText!=null) selectionText.Text=Convert.ToString(checks.CurrentRow.Cells[0].Value)+"  ·  "+Convert.ToString(checks.CurrentRow.Cells[1].Value)+"\n"+Convert.ToString(checks.CurrentRow.Cells[2].Value); };
            layout.Controls.Add(checks,0,1); var selected=Card(); selected.Margin=new Padding(0,12,0,0); selected.Padding=new Padding(16,12,16,10); selectionText=Label("选择一行查看完整说明。",10.5f,Muted); selectionText.Dock=DockStyle.Fill; selected.Controls.Add(selectionText); layout.Controls.Add(selected,0,2); return p;
        }
        Panel BuildHelp() {
            var p=new Panel(); var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty }; layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); p.Controls.Add(layout);
            var chooser=new FlowLayoutPanel { Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty }; var caption=Label("查看品牌说明",10.5f,Ink); caption.Size=new Size(122,32); caption.TextAlign=ContentAlignment.MiddleLeft; chooser.Controls.Add(caption);
            brands=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=214,Font=new Font("Microsoft YaHei UI",11),AccessibleName="选择接入说明的品牌" }; brands.Items.AddRange(new object[]{"技嘉 · GIGABYTE","华硕 · ASUS","微星 · MSI","华擎 · ASRock","其他品牌"}); brands.SelectedIndexChanged+=delegate { RenderHelp(); }; chooser.Controls.Add(brands); layout.Controls.Add(chooser,0,0);
            helpContext=Label("说明切换不会改变本机适配规则。",10,Muted); helpContext.Dock=DockStyle.Fill; layout.Controls.Add(helpContext,0,1);
            var tabs=new TabControl { Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",10.5f),Padding=new Point(18,8) }; guidance=TextTab(tabs,"接入与验收步骤"); var compatibility=TextTab(tabs,"支持范围"); compatibility.Text=CompatibilityText; layout.Controls.Add(tabs,0,2); brands.SelectedIndex=0; return p;
        }
        RichTextBox TextTab(TabControl parent,string title) { var page=new TabPage(title) { Padding=new Padding(21,18,21,18),BackColor=Color.White }; var text=ReadText(); page.Controls.Add(text); parent.TabPages.Add(page); return text; }
        RichTextBox ReadText() {
            var text=new RichTextBox { Dock=DockStyle.Fill,ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,ForeColor=Ink,Font=new Font("Microsoft YaHei UI",11),DetectUrls=true,ScrollBars=RichTextBoxScrollBars.Vertical,TabStop=true };
            text.LinkClicked+=delegate(object sender,LinkClickedEventArgs e) { Uri u; if(Uri.TryCreate(e.LinkText,UriKind.Absolute,out u)&&u.Scheme=="https") Process.Start(new ProcessStartInfo(u.ToString()){UseShellExecute=true}); }; return text;
        }
        void Navigate(int index) {
            var pages=new[]{overview,detailsPage,helpPage,settingsPage};
            for(int i=0;i<pages.Length;i++) { pages[i].Visible=i==index; nav[i].ForeColor=i==index?Accent:Ink; nav[i].BackColor=i==index?Color.FromArgb(235,241,250):Color.White; }
            pages[index].BringToFront();
        }
        void SetBusy(bool value) {
            if(IsDisposed||Disposing) return;
            busy=value; scan.Enabled=!value; scan.Text=value?"检测中…":"重新检测";
            bool ready=!value&&current!=null&&current.Errors.Count==0, native=current!=null&&NativePolicy.Supports(current.Brand);
            primary.Enabled=ready&&presentation!=null&&presentation.PrimaryAction!="none";
            if(!value&&presentation!=null&&presentation.PrimaryAction=="scan") primary.Enabled=true;
            repair.Enabled=ready&&(native?current.RuleEligible&&current.NativeConfirmed:GigabyteRecovery.CanConnect(current));
            export.Enabled=!value&&current!=null; planButton.Enabled=!value&&current!=null;
            if(value) { undo.Enabled=false; return; }
            bool canUndo=false; try { var e=StateStore.Load(); canUndo=e!=null&&(e.Enabled||e.Changes.Count>0); } catch { canUndo=File.Exists(StateStore.StateFile); }
            undo.Visible=native||canUndo; undo.Enabled=!value&&canUndo; tips.SetToolTip(undo,canUndo?"停止后台维护，撤销本工具修改的设置。":"当前没有需要撤销的维护设置。");
        }
        async Task RefreshSnapshot() {
            if(busy||IsDisposed||Disposing) return; SetBusy(true); var timer=Stopwatch.StartNew(); footer.Text="正在读取硬件、官方组件与当前会话…";
            try { var result=await Task.Run(()=>Probe.Scan()); if(IsDisposed||Disposing) return; current=result; Render(); observed.Text="最近检测 "+DateTime.Now.ToString("HH:mm:ss")+" · 耗时 "+timer.Elapsed.TotalSeconds.ToString("0.00")+" 秒"; }
            catch(Exception ex) {
                if(IsDisposed||Disposing) return; current=null; Render(); health.Text="检测未完成"; healthDetail.Text=ex.Message; health.ForeColor=Color.FromArgb(153,98,21); statusIcon.Text="\uE7BA"; statusIcon.ForeColor=health.ForeColor;
                presentation=new UiState {PrimaryAction="scan",PrimaryText="重新检测"}; primary.Text="重新检测"; nextTitle.Text="先完成一次检测"; nextBody.Text="本次读取失败，尚不能确认连接状态。"; footer.Text="检测失败："+ex.Message;
            } finally { SetBusy(false); }
        }
        void Render() {
            var s=current; presentation=UiState.For(s);
            health.Text=presentation.Title; healthDetail.Text=presentation.Detail; primary.Text=presentation.PrimaryText; primary.AccessibleName=primary.Text;
            float titleSize=presentation.Tone=="success"?30:22; if(health.Font.Size!=titleSize) { var oldFont=health.Font; health.Font=new Font(Font.FontFamily,titleSize,FontStyle.Bold); oldFont.Dispose(); }
            health.ForeColor=presentation.Tone=="success"?Green:presentation.Tone=="warning"?Color.FromArgb(153,98,21):Ink;
            statusIcon.ForeColor=health.ForeColor; statusIcon.Text=presentation.Tone=="success"?"\uE930":presentation.Tone=="warning"?"\uE7BA":"\uE946";
            if(s==null) {
                hardware.Text=hardwareNote.Text=software.Text=softwareNote.Text="尚未确认"; nextTitle.Text="等待检测结果"; nextBody.Text="读取本机连接与官方软件信息。"; persistence.Text="检测与接入指引"; maintenanceBody.Text="根据本机品牌提供接入方式。";
                for(int i=0;i<3;i++) SetStep(i,false); stepText[0].Text="1  检查官方组件"; stepText[1].Text="2  确认当前连接"; stepText[2].Text="3  切换灯效并检查内存"; planButton.Visible=false; checks.Rows.Clear(); selectionText.Text="检测后显示依据。"; return;
            }
            bool native=NativePolicy.Supports(s.Brand), valid=s.Errors.Count==0;
            bool components=valid && (native?s.Requirements.Count>0&&s.Requirements.All(r=>r.Present):s.Brand=="GIGABYTE"&&!String.IsNullOrEmpty(s.GccVersion)&&!String.IsNullOrEmpty(s.DcsVersion));
            bool connected=valid && (native?s.RuleEligible&&s.NativeServices.All(v=>v.State=="Running"):GigabyteRecovery.IsConnected(s));
            SetStep(0,components); SetStep(1,connected); SetStep(2,false);
            stepText[0].Text=components?"1  组件已识别":"1  检查官方组件";
            stepText[1].Text=connected?(native?"2  官方服务已就绪":"2  内存连接已确认"):"2  确认当前连接";
            if(!native && connected) { health.Text="连接正常"; healthDetail.Text="已连接 "+s.Evidence.Modules+" 条内存，当前日志未发现通信错误。"; nextTitle.Text="还差一步：确认灯效"; nextBody.Text="打开官方软件，切换一次灯效。\n确认实体内存同步变化。"; primary.Text=presentation.PrimaryText; }
            else { nextTitle.Text=presentation.NextTitle; nextBody.Text=presentation.NextBody; }
            hardware.Text=s.BoardModel??"主板信息未知"; hardwareNote.Text=s.Ram.Count==0?"未识别":s.Ram.Count+" 条 / "+(s.Ram.Sum(r=>r.Bytes)/(1024L*1024*1024))+" GB";
            software.Text=s.Brand=="GIGABYTE"?"GIGABYTE Control Center":s.Brand=="ASUS"?"Armoury Crate / Aura Sync":s.Brand=="MSI"?"MSI Center / Mystic Light":s.Brand=="ASRock"?"Polychrome RGB":"未适配此品牌";
            softwareNote.Text=s.Brand=="GIGABYTE"?(s.GccVersion??"未检测到"):s.Brand=="ASUS"||s.Brand=="MSI"?"详见检测详情":"尚未确认";
            persistence.Text=native?(s.AssistantEnabled?s.AssistantState:"服务维护尚未开启"):s.Brand=="GIGABYTE"?"按需连接，无常驻维护":"检测与接入指引";
            maintenanceBody.Text=native?"组件变化时暂停自动恢复。可随时撤销本工具设置。":s.AssistantEnabled?"存在旧版维护记录，可撤销本工具设置。":s.Brand=="GIGABYTE"?"点击连接后检查本次会话。可在设置中选择成功后退出。":"本品牌不启用自动维护。";
            planButton.Visible=native||s.Brand=="GIGABYTE"; undo.Visible=native||s.AssistantEnabled;
            repair.Text=native?"恢复基线服务":s.Brand=="GIGABYTE"?"连接内存":"此品牌仅检测";
            tips.SetToolTip(repair,s.Brand=="GIGABYTE"?GigabyteRecovery.ConnectionBlockReason(s):"恢复已经确认的基线服务。");
            tips.SetToolTip(hardware,String.Join("\n",s.Ram.Select(x=>x.Part))); tips.SetToolTip(software,software.Text);
            FillChecks(s); int brand=s.Brand=="GIGABYTE"?0:s.Brand=="ASUS"?1:s.Brand=="MSI"?2:s.Brand=="ASRock"?3:4; if(brands.SelectedIndex==brand) RenderHelp(); else brands.SelectedIndex=brand;
            footer.Text=(lastOperation==null?"":lastOperation+" · ")+"F5 重新检测 · 数据保存在本机";
        }
        void SetStep(int index,bool complete) { stepIcon[index].Text=complete?"\uE930":"\uE946"; stepIcon[index].ForeColor=complete?Green:index==2?Accent:Muted; }
        void AddCheck(string key,string value,string note) { checks.Rows.Add(key,value,note); }
        void FillChecks(Snapshot s) {
            checks.Rows.Clear(); AddCheck("海盗船内存",String.Join(" / ",s.Ram.Select(r=>r.Part).Distinct()),s.Ram.Count+" 条内存。型号来自系统信息，不能证明灯效已接管。");
            if(NativePolicy.Supports(s.Brand)) {
                foreach(var r in s.Requirements) AddCheck(r.Name,r.Present?"已检测到":"待补齐",r.Detail);
                foreach(var v in s.NativeServices) AddCheck(v.DisplayName??v.Name,ServiceState(v.State)+" / "+(v.StartMode=="Auto"?"自动启动":v.StartMode),(v.Trusted?"厂商签名通过。":"签名或路径未通过。")+"服务："+v.Name);
                AddCheck("iCUE 状态",s.IcueRunning?"运行中":"未运行","此品牌适配保留 iCUE 原有设置。");
                AddCheck("接管确认记录",s.NativeConfirmed?"已保存":"尚未确认",s.NativeConfirmed?"用户确认时间（UTC）："+s.NativeConfirmedUtc:"先在官方软件确认内存能跟随，再启用持久维护。");
            } else if(s.Brand=="GIGABYTE") {
                AddCheck("GCC 会话",s.GccPid>0?"正在运行":"未运行",s.GccPid>0?"当前进程 PID "+s.GccPid:"尚无活动会话。后台检查不会重新打开已退出的软件。");
                AddCheck("Corsair 设备服务",ServiceState(s.DcsState),"按需启动或暂时停止本身不代表故障。");
                AddCheck("当前连接日志",s.Evidence.CurrentSession?"已匹配当前进程":"尚未确认",s.Evidence.CurrentSession?"模块 "+s.Evidence.Modules+"；内存写入错误 "+s.Evidence.InstalledWriteFailures+"；通信错误 "+s.Evidence.PipeErrors:"不使用旧进程或上次开机的日志判断当前状态。");
                AddCheck("iCUE 状态",s.IcueRunning?"运行中":"未运行","iCUE 运行时不执行技嘉连接，避免控制冲突。"); AddCheck("GCC 开机任务",s.GccAutostart?"已启用":"尚未确认","保留厂商原有启动任务。");
                AddCheck("手动连接",GigabyteRecovery.CanConnect(s)?"可执行":"未开放",GigabyteRecovery.ConnectionBlockReason(s));
            }
            AddCheck("适配规则",s.RuleEligible?"条件匹配":"仅检测",s.RuleReason); AddCheck("重启与实体灯效","待人工验证","日志或服务可用不能替代实体同步验收。"); foreach(string error in s.Errors) AddCheck("检测信息缺口","读取失败",error);
            if(checks.Rows.Count>0) checks.Rows[0].Selected=true;
        }
        static string ServiceState(string value) { return value=="Running"?"运行中":value=="Stopped"?"已停止":value??"未检测到"; }
        void RenderHelp() { if(brands==null||guidance==null) return; string[] codes={"GIGABYTE","ASUS","MSI","ASRock","Other"}; int index=Math.Max(0,brands.SelectedIndex); string code=codes[index]; guidance.Text=Guidance.For(code)+"\n\n接管后的验收\n\n1. 在官方软件中确认内存可见，并切换一次灯效检查实体内存。\n2. 保存工作，自行重启 Windows 再检查。\n3. 再分别验证关机后开机、睡眠唤醒。\n4. 若出现问题，在检测详情中导出报告。"; helpContext.Text=current!=null&&current.Brand!=code?"正在查看其他品牌说明；本机仍按 "+current.Brand+" 检测与适配。":"对应当前主板的接入说明。界面名称可能随官方软件版本变化。"; }
        async Task PrimaryAction() { if(busy||presentation==null) return; string action=presentation.PrimaryAction; if(action=="scan") await RefreshSnapshot(); else if(action=="help") Navigate(2); else if(action=="details") Navigate(1); else if(action=="enable") await Action("--enable"); else if(action=="repair") await Action("--repair"); }
        void ShowPlan() {
            if(current==null) return; using(var dialog=new Form { Text="执行方案 · "+(current.Brand??"当前品牌"),StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(710,530),MinimumSize=new Size(620,450),Font=Font,BackColor=Color.White,ShowInTaskbar=false,MinimizeBox=false,MaximizeBox=false,Padding=new Padding(24) }) {
                var text=ReadText(); text.Text=Operations.Plan(current)+"\n\n撤销只恢复本工具修改的设置。安装与运行记录保存在本机。"; var close=MakeButton("知道了",true); close.Dock=DockStyle.Bottom; close.DialogResult=DialogResult.OK; dialog.Controls.Add(text); dialog.Controls.Add(close); dialog.AcceptButton=close; dialog.CancelButton=close; dialog.ShowDialog(this);
            }
        }
        async Task Action(string command) {
            if(busy||current==null) return;
            bool connecting=command=="--repair"&&current.Brand=="GIGABYTE";
            if(connecting&&MessageBox.Show(this,Operations.Plan(current)+"\n\n现在执行一次连接？","连接内存",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
            if(command=="--enable"&&NativePolicy.Supports(current.Brand)) {
                if(MessageBox.Show(this,"请确认已经在 "+NativePolicy.Product(current.Brand)+" 完成以下检查：\n\n1. 内存显示在官方软件的同步组中。\n2. 切换灯效时，实体内存确实跟随。\n\n启用后会记录这次确认，并维护当前官方服务。工具无法自动判断实体灯效。\n\n以上检查是否已完成？","确认首次接管结果",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
                command="--enable-confirmed "+current.Fingerprint;
            }
            SetBusy(true); scan.Text="操作中…"; footer.Text="正在执行，Windows 可能请求管理员权限…";
            bool succeeded=false; string completionMessage=null;
            try { string resultPath=Path.Combine(StateStore.Root,"last-result-"+StateStore.Sid+".json"); DateTime started=DateTime.UtcNow;
                using(var p=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,command){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})) {
                    var elapsed=Stopwatch.StartNew(); using(var progress=new System.Windows.Forms.Timer {Interval=1000}) { progress.Tick+=delegate { if(!IsDisposed&&!Disposing) footer.Text="正在等待"+(connecting?"本次连接确认":"操作完成")+" · 已用 "+(int)elapsed.Elapsed.TotalSeconds+" 秒"; }; progress.Start(); await Task.Run(()=>p.WaitForExit()); progress.Stop(); }
                    if(IsDisposed||Disposing) return;
                    succeeded=p.ExitCode==0; completionMessage=succeeded?"操作完成。":"操作未完成，请查看检测结果。";
                    if(File.Exists(resultPath)&&File.GetLastWriteTimeUtc(resultPath)>=started) {
                        var result=Json.Read<OperationResult>(File.ReadAllText(resultPath));
                        if(result==null||result.Success!=succeeded) throw new InvalidDataException("操作结果与退出状态不一致，请重新检测。");
                        completionMessage=result.Message;
                    }
                    lastOperation=succeeded?"上次操作已完成":"上次操作未完成";
                }
            } catch(System.ComponentModel.Win32Exception ex) { if(IsDisposed||Disposing) return; MessageBox.Show(this,ex.NativeErrorCode==1223?"管理员授权已取消，未执行操作。":ex.Message,"操作未执行"); }
            catch(Exception ex) { succeeded=false; completionMessage=null; if(IsDisposed||Disposing) return; MessageBox.Show(this,ex.Message,"操作未完成"); }
            finally { SetBusy(false); }
            await RefreshSnapshot();
            if(IsDisposed||Disposing) return;
            if(ShouldExitAfterConnection(command,succeeded)) { Close(); return; }
            if(completionMessage!=null) {
                if(connecting&&succeeded&&!GigabyteRecovery.IsConnected(current)) { completionMessage="连接操作已结束，但最新检测未确认连接。请查看检测详情。"; succeeded=false; }
                MessageBox.Show(this,completionMessage,"接管助手",MessageBoxButtons.OK,succeeded?MessageBoxIcon.Information:MessageBoxIcon.Warning);
            }
        }
        void Export() { if(current==null) return; using(var d=new SaveFileDialog { Filter="JSON 检测报告|*.json",FileName="Corsair接管检测-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json",OverwritePrompt=true }) if(d.ShowDialog(this)==DialogResult.OK) { File.WriteAllText(d.FileName,Json.Write(current),System.Text.Encoding.UTF8); footer.Text="检测报告已保存到所选位置。"; } }
        protected override void Dispose(bool disposing) { if(disposing) { tips.Dispose(); if(tray!=null) { tray.Visible=false; tray.Dispose(); } if(trayMenu!=null) trayMenu.Dispose(); if(identityImage!=null) identityImage.Dispose(); if(applicationIcon!=null) applicationIcon.Dispose(); } base.Dispose(disposing); }
        const string CompatibilityText="支持范围\n\n华硕 · Aura Sync\n检测官方组件，维护已确认的自动服务基线。\n\n微星 · Mystic Light\n检测官方组件，维护已确认的自动服务基线。\n\n以上均为预览适配，需在官方软件中确认实际控制。\n\n技嘉 · RGB Fusion\n已适配组合提供按需连接，按当前会话日志确认结果。具体条件见执行方案，不启用技嘉常驻维护。\n\n华擎及其他品牌\n提供检测与接入指引。\n\n本工具不设置灯效、不安装驱动、不修改 BIOS。\n\nF5：重新检测\nAlt + 1 / 2 / 3 / 4：切换页面";
    }
}
