using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CorsairTakeover {
    public sealed class Surface : Panel {
        public Color Line=Color.FromArgb(221,229,237);
        public Surface() { DoubleBuffered=true; BackColor=Color.White; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using(var pen=new Pen(Line)) e.Graphics.DrawRectangle(pen,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1)); }
    }
    public sealed class MainForm : Form {
        static readonly Color Ink=Color.FromArgb(28,43,61),Muted=Color.FromArgb(86,103,124),Accent=Color.FromArgb(32,100,195),Canvas=Color.FromArgb(244,247,251),Navy=Color.FromArgb(22,37,58);
        Panel pageHost,overview,detailsPage,helpPage; Surface hero;
        Label pageTitle,pageSubtitle,health,healthDetail,evidence,hardware,software,persistence,hardwareNote,softwareNote,persistenceNote,nextTitle,nextBody,maintenanceBody,footer,helpContext,selectionText;
        Button[] nav; Button scan,primary,planButton,undo,export,repair;
        DataGridView checks; RichTextBox guidance; ComboBox brands;
        Snapshot current; UiState presentation; bool busy,layoutReady; string lastOperation; TableLayoutPanel overviewLayout;
        readonly ToolTip tips=new ToolTip { AutoPopDelay=18000,InitialDelay=400,ReshowDelay=150 };

        public MainForm() {
            SuspendLayout(); AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi;
            Text="Corsair 内存接管助手 · 0.3.1"; Font=new Font("Microsoft YaHei UI",10.5f); ClientSize=new Size(1160,800); MinimumSize=new Size(980,660);
            StartPosition=FormStartPosition.CenterScreen; ForeColor=Ink; BackColor=Canvas; Icon=SystemIcons.Application; KeyPreview=true;
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,194)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); Controls.Add(root);
            var sidebar=new Panel { Dock=DockStyle.Fill,BackColor=Navy,Margin=Padding.Empty,Padding=new Padding(18,28,18,20) }; root.Controls.Add(sidebar,0,0);
            var identityPanel=new Panel { Dock=DockStyle.Top,Height=88 };
            var identity=Label("接管助手",22,Color.White,FontStyle.Bold); identity.SetBounds(0,0,158,43); identityPanel.Controls.Add(identity);
            var identityCaption=Label("CORSAIR MEMORY",9,Color.FromArgb(181,199,219)); identityCaption.SetBounds(2,48,156,24); identityPanel.Controls.Add(identityCaption); sidebar.Controls.Add(identityPanel);
            var navPanel=new FlowLayoutPanel { FlowDirection=FlowDirection.TopDown,WrapContents=false,Dock=DockStyle.Top,Height=178,Padding=new Padding(0,14,0,0) };
            nav=new Button[3]; string[] navNames={"接管概览","检测详情","接入与帮助"};
            for(int i=0;i<3;i++) { int index=i; nav[i]=MakeButton(navNames[i],false); nav[i].Width=158; nav[i].Height=46; nav[i].Margin=new Padding(0,0,0,8); nav[i].TextAlign=ContentAlignment.MiddleLeft; nav[i].Padding=new Padding(14,0,0,0); nav[i].Click+=delegate { Navigate(index); }; navPanel.Controls.Add(nav[i]); }
            sidebar.Controls.Add(navPanel); navPanel.BringToFront();
            var sidebarBottom=Label("v0.3.1  预览版\n\n检测数据仅保存在本机",9.5f,Color.FromArgb(181,199,219)); sidebarBottom.Dock=DockStyle.Bottom; sidebarBottom.Height=74; sidebar.Controls.Add(sidebarBottom);
            var main=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(26,22,26,12),Margin=Padding.Empty };
            main.RowStyles.Add(new RowStyle(SizeType.Absolute,76)); main.RowStyles.Add(new RowStyle(SizeType.Percent,100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); root.Controls.Add(main,1,0);
            var header=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty };
            pageTitle=Label("接管概览",21,Ink,FontStyle.Bold); pageTitle.SetBounds(0,0,500,38);
            pageSubtitle=Label("查看内存连接，并保持官方软件接管。",10.5f,Muted); pageSubtitle.SetBounds(0,43,650,25);
            scan=MakeButton("重新检测",false); scan.Size=new Size(116,40); scan.Anchor=AnchorStyles.Right|AnchorStyles.Top; header.Controls.Add(scan); header.Controls.Add(pageTitle); header.Controls.Add(pageSubtitle); header.Resize+=delegate { scan.Location=new Point(header.ClientSize.Width-scan.Width,5); pageSubtitle.Width=Math.Max(300,header.Width-140); }; scan.Click+=async delegate { await RefreshSnapshot(); }; main.Controls.Add(header,0,0);
            pageHost=new Panel { Dock=DockStyle.Fill,Margin=Padding.Empty }; main.Controls.Add(pageHost,0,1);
            footer=Label("首次检测只读取本机信息。",9.5f,Muted); footer.Dock=DockStyle.Fill; footer.TextAlign=ContentAlignment.BottomLeft; main.Controls.Add(footer,0,2);
            overview=BuildOverview(); detailsPage=BuildDetails(); helpPage=BuildHelp();
            foreach(var p in new[]{overview,detailsPage,helpPage}) { p.Dock=DockStyle.Fill; pageHost.Controls.Add(p); }
            Navigate(0); ResumeLayout(true);
            Shown+=async delegate { layoutReady=true; FitOverview(); var area=Screen.FromControl(this).WorkingArea; if(Width>area.Width-32||Height>area.Height-32) { Size=new Size(Math.Min(Width,area.Width-32),Math.Min(Height,area.Height-32)); Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2); } await RefreshSnapshot(); };
            KeyDown+=async delegate(object sender,KeyEventArgs e) { if(e.KeyCode==Keys.F5&&!busy) { e.Handled=true; await RefreshSnapshot(); } else if(e.Alt&&e.KeyCode>=Keys.D1&&e.KeyCode<=Keys.D3) { Navigate((int)e.KeyCode-(int)Keys.D1); e.Handled=true; } };
        }
        static Label Label(string text,float size,Color color,FontStyle style=FontStyle.Regular) { return new Label { Text=text,Font=new Font("Microsoft YaHei UI",size,style),ForeColor=color,AutoSize=false,UseMnemonic=false,Margin=Padding.Empty }; }
        Button MakeButton(string text,bool primaryStyle) {
            var b=new Button { Text=text,Height=42,Width=144,FlatStyle=FlatStyle.Flat,Font=new Font("Microsoft YaHei UI",10.5f),BackColor=primaryStyle?Accent:Color.White,ForeColor=primaryStyle?Color.White:Ink,Cursor=Cursors.Hand,UseVisualStyleBackColor=false,Margin=new Padding(0,0,10,0),AccessibleName=text };
            b.FlatAppearance.BorderColor=primaryStyle?Accent:Color.FromArgb(205,216,228); b.FlatAppearance.MouseOverBackColor=primaryStyle?Color.FromArgb(24,81,158):Color.FromArgb(235,242,250); b.FlatAppearance.MouseDownBackColor=primaryStyle?Color.FromArgb(19,65,130):Color.FromArgb(221,234,247); return b;
        }
        Surface Card() { return new Surface { Dock=DockStyle.Fill,Padding=new Padding(20),Margin=Padding.Empty }; }
        Panel BuildOverview() {
            var p=new Panel { AutoScroll=true }; var layout=new TableLayoutPanel { Dock=DockStyle.Top,ColumnCount=1,RowCount=4,Margin=Padding.Empty,MinimumSize=new Size(0,620),Height=620 };
            overviewLayout=layout; p.ClientSizeChanged+=delegate { FitOverview(); };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,174)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,146)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,57)); p.Controls.Add(layout);
            hero=Card(); hero.Padding=new Padding(24,18,24,18); hero.Margin=new Padding(0,0,0,16);
            var heroLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Margin=Padding.Empty };
            foreach(int h in new[]{25,42,40,23}) heroLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
            heroLayout.Controls.Add(Label("当前接管状态",10,Muted),0,0);
            health=Label("正在读取本机状态…",23,Ink,FontStyle.Bold); health.Dock=DockStyle.Fill; heroLayout.Controls.Add(health,0,1);
            healthDetail=Label("首次检测不会修改系统设置。",10.5f,Muted); healthDetail.Dock=DockStyle.Fill; heroLayout.Controls.Add(healthDetail,0,2);
            evidence=Label("正在检测",9.5f,Muted); evidence.Dock=DockStyle.Fill; heroLayout.Controls.Add(evidence,0,3); hero.Controls.Add(heroLayout); layout.Controls.Add(hero,0,0);
            var metrics=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=new Padding(0,0,0,16) };
            for(int i=0;i<3;i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.333f));
            BuildMetric(metrics,0,"主板与内存",out hardware,out hardwareNote); BuildMetric(metrics,1,"官方控制软件",out software,out softwareNote); BuildMetric(metrics,2,"持久维护",out persistence,out persistenceNote); layout.Controls.Add(metrics,0,1);
            var lower=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty }; lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60)); lower.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
            var next=Card(); next.Margin=new Padding(0,0,12,0); var nextLayout=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Margin=Padding.Empty };
            nextLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,27)); nextLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,34)); nextLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); nextLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            nextLayout.Controls.Add(Label("建议下一步",10,Muted),0,0); nextTitle=Label("检查完成后显示下一步",14,Ink,FontStyle.Bold); nextTitle.Dock=DockStyle.Fill; nextLayout.Controls.Add(nextTitle,0,1); nextBody=Label("",10.5f,Muted); nextBody.Dock=DockStyle.Fill; nextBody.Padding=new Padding(0,4,0,8); nextLayout.Controls.Add(nextBody,0,2);
            primary=MakeButton("正在检测…",true); primary.Width=190; primary.Click+=async delegate { await PrimaryAction(); }; nextLayout.Controls.Add(primary,0,3); next.Controls.Add(nextLayout); lower.Controls.Add(next,0,0);
            var maintenance=Card(); var maintenanceLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=Padding.Empty };
            foreach(var r in new[]{new RowStyle(SizeType.Absolute,27),new RowStyle(SizeType.Absolute,32),new RowStyle(SizeType.Percent,100),new RowStyle(SizeType.Absolute,43),new RowStyle(SizeType.Absolute,31)}) maintenanceLayout.RowStyles.Add(r);
            maintenanceLayout.Controls.Add(Label("维护方式",10,Muted),0,0); var maintenanceTitle=Label("登录与唤醒后检查",13,Ink,FontStyle.Bold); maintenanceTitle.Dock=DockStyle.Fill; maintenanceLayout.Controls.Add(maintenanceTitle,0,1); maintenanceBody=Label("仅在你启用后运行。\n组件变化时暂停自动恢复。",10.5f,Muted); maintenanceBody.Dock=DockStyle.Fill; maintenanceBody.Padding=new Padding(0,4,0,5); maintenanceLayout.Controls.Add(maintenanceBody,0,2);
            planButton=MakeButton("查看执行方案",false); planButton.Width=155; planButton.Click+=delegate { ShowPlan(); }; maintenanceLayout.Controls.Add(planButton,0,3);
            undo=MakeButton("撤销本工具设置",false); undo.Width=164; undo.Height=30; undo.Font=new Font("Microsoft YaHei UI",9.5f); undo.FlatAppearance.BorderSize=0; undo.ForeColor=Muted; undo.TextAlign=ContentAlignment.MiddleLeft; undo.Click+=async delegate { await Action("--restore"); }; maintenanceLayout.Controls.Add(undo,0,4); maintenance.Controls.Add(maintenanceLayout); lower.Controls.Add(maintenance,1,0); layout.Controls.Add(lower,0,2);
            var note=Label("实体同步仍需确认：在官方软件中切换一次灯效，并检查重启、唤醒后内存是否仍可控。",10,Muted); note.Dock=DockStyle.Fill; note.Padding=new Padding(2,17,0,0); layout.Controls.Add(note,0,3); return p;
        }
        void BuildMetric(TableLayoutPanel parent,int index,string title,out Label value,out Label note) {
            var card=Card(); card.Padding=new Padding(17,14,17,12); card.Margin=new Padding(0,0,index<2?12:0,0);
            var grid=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty }; grid.RowStyles.Add(new RowStyle(SizeType.Absolute,27)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,100)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,24));
            grid.Controls.Add(Label(title,9.5f,Muted),0,0); value=Label("读取中…",12,Ink,FontStyle.Bold); value.Dock=DockStyle.Fill; grid.Controls.Add(value,0,1); note=Label("",9.5f,Muted); note.Dock=DockStyle.Fill; grid.Controls.Add(note,0,2); card.Controls.Add(grid); parent.Controls.Add(card,index,0);
        }
        void FitOverview() { if(!layoutReady||overview==null||overviewLayout==null) return; int height=Math.Max(overviewLayout.MinimumSize.Height,overview.ClientSize.Height); if(overviewLayout.Height!=height) overviewLayout.Height=height; }
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
            if(overview==null||detailsPage==null||helpPage==null) return;
            overview.Visible=index==0; detailsPage.Visible=index==1; helpPage.Visible=index==2;
            var panels=new[]{overview,detailsPage,helpPage}; panels[index].BringToFront();
            string[] titles={"接管概览","检测详情","接入与帮助"}, subtitles={"查看内存连接，并保持官方软件接管。","查看检测依据、完整说明，或导出报告。","按品牌完成首次接入，再验证重启后的同步。"}; pageTitle.Text=titles[index]; pageSubtitle.Text=subtitles[index];
            for(int i=0;i<nav.Length;i++) { nav[i].BackColor=i==index?Color.FromArgb(43,68,101):Navy; nav[i].ForeColor=i==index?Color.White:Color.FromArgb(183,200,221); nav[i].FlatAppearance.BorderSize=0; nav[i].FlatAppearance.MouseOverBackColor=Color.FromArgb(52,79,114); nav[i].Font=new Font("Microsoft YaHei UI",11,i==index?FontStyle.Bold:FontStyle.Regular); }
        }
        void SetBusy(bool value) {
            busy=value; scan.Enabled=!value; scan.Text=value?"检测中…":"重新检测";
            bool ready=!value&&current!=null&&current.Errors.Count==0, native=current!=null&&NativePolicy.Supports(current.Brand);
            primary.Enabled=ready&&presentation!=null&&presentation.PrimaryAction!="none";
            if(!value&&presentation!=null&&presentation.PrimaryAction=="scan") primary.Enabled=true;
            repair.Enabled=ready&&native&&current.RuleEligible&&current.NativeConfirmed;
            export.Enabled=!value&&current!=null; planButton.Enabled=!value&&current!=null;
            bool canUndo=false; try { var e=StateStore.Load(); canUndo=e!=null&&(e.Enabled||e.Changes.Count>0); } catch { canUndo=File.Exists(StateStore.StateFile); }
            undo.Enabled=!value&&canUndo; tips.SetToolTip(undo,canUndo?"停止后台维护，撤销本工具修改的设置。":"当前没有需要撤销的维护设置。");
        }
        async Task RefreshSnapshot() {
            if(busy) return; SetBusy(true); footer.Text="正在读取硬件、软件与当前会话信息…";
            try { current=await Task.Run(()=>Probe.Scan()); Render(); }
            catch(Exception ex) { current=null; presentation=new UiState { PrimaryAction="scan",PrimaryText="重新检测" }; health.Text="检测未完成"; healthDetail.Text=ex.Message; hero.BackColor=Color.FromArgb(255,248,235); hero.Line=Color.FromArgb(234,213,175); health.ForeColor=Color.FromArgb(131,86,22); hero.Invalidate(); evidence.Text="本次检测未完成"; hardware.Text=software.Text=persistence.Text="本次未确认"; hardwareNote.Text=softwareNote.Text=persistenceNote.Text=""; checks.Rows.Clear(); selectionText.Text="重新检测后显示依据。"; nextTitle.Text="重新检测本机状态"; nextBody.Text="本次没有完整的检测结果，维护操作暂不可用。"; primary.Text="重新检测"; footer.Text="检测失败："+ex.Message; }
            finally { SetBusy(false); }
        }
        void Render() {
            var s=current; presentation=UiState.For(s); health.Text=presentation.Title; healthDetail.Text=presentation.Detail; evidence.Text=presentation.Evidence+"   ·   实体同步与重启验收需人工确认";
            hero.BackColor=presentation.Tone=="success"?Color.FromArgb(239,249,245):presentation.Tone=="warning"?Color.FromArgb(255,248,235):Color.FromArgb(237,244,253);
            hero.Line=presentation.Tone=="success"?Color.FromArgb(190,224,211):presentation.Tone=="warning"?Color.FromArgb(234,213,175):Color.FromArgb(198,217,243);
            health.ForeColor=presentation.Tone=="success"?Color.FromArgb(22,98,71):presentation.Tone=="warning"?Color.FromArgb(131,86,22):Ink; hero.Invalidate();
            hardware.Text=s.BoardModel??"主板信息未知"; hardwareNote.Text=s.Ram.Count+" 条内存  /  "+(s.Ram.Sum(r=>r.Bytes)/(1024L*1024*1024))+" GB";
            software.Text=s.Brand=="GIGABYTE"?"GIGABYTE Control Center":s.Brand=="ASUS"?"Armoury Crate / Aura Sync":s.Brand=="MSI"?"MSI Center / Mystic Light":s.Brand=="ASRock"?"Polychrome RGB":"未适配此品牌";
            softwareNote.Text=s.Brand=="GIGABYTE"?(s.GccVersion??"未检测到"):(s.Brand=="ASUS"||s.Brand=="MSI"?"适配预览 · 待对应主板实测":"当前提供检测与说明");
            persistence.Text=s.AssistantEnabled?((s.AssistantState??"").Contains("暂停")?"已暂停":"已开启"):"尚未开启"; persistenceNote.Text=s.AssistantEnabled?"登录与唤醒后检查":"由你主动启用";
            nextTitle.Text=presentation.NextTitle; nextBody.Text=presentation.NextBody; primary.Text=presentation.PrimaryText; primary.AccessibleName=presentation.PrimaryText;
            maintenanceBody.Text=s.AssistantEnabled?"组件变化时暂停自动恢复。\n可随时撤销本工具设置。":"仅在你启用后运行。\n组件变化时暂停自动恢复。";
            if(!NativePolicy.Supports(s.Brand)) {
                persistence.Text=s.AssistantEnabled?"存在旧版维护":"仅检测";
                persistenceNote.Text=s.AssistantEnabled?"可撤销旧版任务":"此品牌不启用维护";
                maintenanceBody.Text=s.AssistantEnabled?"旧版维护任务仍有记录。可使用撤销移除。":"此品牌提供检测和接入指引。";
            }
            repair.Text=NativePolicy.Supports(s.Brand)?"恢复基线服务":"此品牌仅检测";
            tips.SetToolTip(hardware,String.Join("\n",s.Ram.Select(x=>x.Part))); tips.SetToolTip(software,software.Text+"\n"+softwareNote.Text);
            FillChecks(s); int brand=s.Brand=="GIGABYTE"?0:s.Brand=="ASUS"?1:s.Brand=="MSI"?2:s.Brand=="ASRock"?3:4; brands.SelectedIndex=brand; RenderHelp();
            footer.Text=(lastOperation??("最近检测 "+DateTime.Now.ToString("HH:mm:ss")))+"   ·   F5 重新检测   ·   数据仅保存在本机";
        }
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
                AddCheck("iCUE 状态",s.IcueRunning?"运行中":"未运行","仅显示运行状态；此品牌不执行自动恢复。"); AddCheck("GCC 开机任务",s.GccAutostart?"已启用":"尚未确认","保留厂商原有启动任务。");
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
            if(command=="--enable"&&NativePolicy.Supports(current.Brand)) {
                if(MessageBox.Show(this,"请确认已经在 "+NativePolicy.Product(current.Brand)+" 完成以下检查：\n\n1. 内存显示在官方软件的同步组中。\n2. 切换灯效时，实体内存确实跟随。\n\n启用后会记录这次确认，并维护当前官方服务。工具无法自动判断实体灯效。\n\n以上检查是否已完成？","确认首次接管结果",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
                command="--enable-confirmed "+current.Fingerprint;
            }
            SetBusy(true); scan.Text="操作中…"; footer.Text="正在执行，Windows 可能请求管理员权限…";
            try { string resultPath=Path.Combine(StateStore.Root,"last-result-"+StateStore.Sid+".json"); DateTime started=DateTime.UtcNow;
                using(var p=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,command){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})) { await Task.Run(()=>p.WaitForExit()); string message=p.ExitCode==0?"操作完成。":"操作未完成，请查看检测结果。"; if(File.Exists(resultPath)&&File.GetLastWriteTimeUtc(resultPath)>=started.AddSeconds(-2)) message=Json.Read<OperationResult>(File.ReadAllText(resultPath)).Message; lastOperation=p.ExitCode==0?"上次操作已完成":"上次操作未完成"; MessageBox.Show(this,message,"接管助手",MessageBoxButtons.OK,p.ExitCode==0?MessageBoxIcon.Information:MessageBoxIcon.Warning); }
            } catch(System.ComponentModel.Win32Exception ex) { MessageBox.Show(this,ex.NativeErrorCode==1223?"管理员授权已取消，未执行操作。":ex.Message,"操作未执行"); }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,"操作未完成"); }
            finally { SetBusy(false); }
            await RefreshSnapshot();
        }
        void Export() { if(current==null) return; using(var d=new SaveFileDialog { Filter="JSON 检测报告|*.json",FileName="Corsair接管检测-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json",OverwritePrompt=true }) if(d.ShowDialog(this)==DialogResult.OK) { File.WriteAllText(d.FileName,Json.Write(current),System.Text.Encoding.UTF8); footer.Text="检测报告已保存到所选位置。"; } }
        protected override void Dispose(bool disposing) { if(disposing) tips.Dispose(); base.Dispose(disposing); }
        const string CompatibilityText="支持范围\n\n华硕 · Aura Sync\n检测官方组件，维护已确认的自动服务基线。\n\n微星 · Mystic Light\n检测官方组件，维护已确认的自动服务基线。\n\n以上均为预览适配，需在官方软件中确认实际控制。\n\n技嘉、华擎及其他品牌\n提供检测与接入指引，不执行自动恢复。\n\n本工具不设置灯效、不安装驱动、不修改 BIOS。\n\nF5：重新检测\nAlt + 1 / 2 / 3：切换页面";
    }
}
