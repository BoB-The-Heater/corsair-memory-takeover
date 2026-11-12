using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using CorsairTakeover;

public sealed class FakeStore : IValueStore {
    public RegistryValue Value;
    public RegistryValue Read(string key,string name) { return Value; }
    public void Write(RegistryValue value) { Value=value; }
}
public sealed class FakeServices : IServiceStarter {
    public List<string> Started=new List<string>(); public bool Fail;
    public void StartStopped(NativeService s) { if(Fail) throw new InvalidOperationException("simulated SCM failure"); Started.Add(s.Name); }
}
public sealed class FakeTaskAction {
    public int Type { get; set; }
    public string Path { get; set; }
}
public sealed class FakeTaskActions {
    public List<FakeTaskAction> Values = new List<FakeTaskAction>();
    public int Count { get { return Values.Count; } }
    public FakeTaskAction this[int index] { get { return Values[index - 1]; } }
}
public sealed class FakeTaskDefinition {
    public FakeTaskActions Actions { get; set; }
}
public sealed class FakeTask {
    public bool Enabled { get; set; }
    public int DefinitionReads;
    public FakeTaskDefinition Value = new FakeTaskDefinition { Actions = new FakeTaskActions() };
    public FakeTaskDefinition Definition { get { DefinitionReads++; return Value; } }
}
public sealed class FakeTaskScheduler {
    public FakeTask Task = new FakeTask { Enabled = true };
    public Exception Failure;
    public Exception TaskFailure;
    public void Connect(object server=null, object user=null, object domain=null, object password=null) {
        if(Failure!=null) throw Failure;
    }
    public FakeTaskScheduler GetFolder(string path) {
        if(path != "\\") throw new InvalidOperationException("Only the root folder may be queried.");
        return this;
    }
    public FakeTask GetTask(string name) {
        if(name != "GCC") throw new InvalidOperationException("Only the existing GCC task may be queried.");
        if(TaskFailure!=null) throw TaskFailure;
        return Task;
    }
}
internal sealed class FakeGigabyteConnection : IGigabyteConnection {
    public Queue<Snapshot> Observations=new Queue<Snapshot>();
    public Snapshot Current,Final;
    public int Restarts,Reads,Verifications,Waits;
    public TimeSpan Elapsed { get; private set; }
    public DateTime Restart(Snapshot baseline) { Restarts++; return DateTime.Parse("2000-01-01T01:02:00Z").ToUniversalTime(); }
    public Snapshot Observe(Snapshot baseline) { Reads++; if(Observations.Count>0) Current=Observations.Dequeue(); return Current; }
    public Snapshot Verify() { Verifications++; return Final??Current; }
    public void Wait(TimeSpan delay) { Waits++; Elapsed+=delay; }
}
public static class Tests {
    static int count;
    static void Assert(bool condition,string message) { if(!condition) throw new Exception("FAIL: "+message); count++; Console.WriteLine("PASS: "+message); }
    static string Session(int pid,string extra) {
        return "2000-01-01 01:01:00 I svc.server: New client connected: \"GCC.exe\" ( \"\\\\.\\pipe\\:pid:"+pid+"\\{test}\" )\n"+
            "2000-01-01 01:01:00 W svc.lla: Byte write failed: dram=[1:0] offset=SMBSpeed\n"+extra+
            "2000-01-01 01:01:01 I svc.lla: Detected module [1:1]: PN=SYNTHETIC-RAM; isCorsair=true; hasLightings=true; pid=0x201\n"+
            "2000-01-01 01:01:01 I svc.lla: Detected module [1:4]: PN=SYNTHETIC-RAM; isCorsair=true; hasLightings=true; pid=0x201\n"+
            "2000-01-01 01:01:02 I svc.server.rp: Registered new client with priority 0 \"GCC.exe\" ( \"\\\\.\\pipe\\:pid:"+pid+"\\{test}\" )\n";
    }
    static Snapshot GccSample() {
        return new Snapshot { Brand="GIGABYTE",BoardModel="Synthetic board",Fingerprint="sample",GccPid=42,DcsState="Running",BootUtc="2000-01-01T01:00:00Z",GccStartUtc="2000-01-01T01:00:00Z",
            Ram=new List<RamInfo>{new RamInfo{Vendor="Corsair",Part="SYNTHETIC-RAM"},new RamInfo{Vendor="Corsair",Part="SYNTHETIC-RAM"}} };
    }
    static Snapshot Native(string brand) {
        return new Snapshot { Brand=brand,Fingerprint="fixture",IcueRunning=true,IcueVersion="fixture",Ram=new List<RamInfo>{new RamInfo{Vendor="Corsair",Part="SYNTHETIC-RAM"}},
            Requirements=new List<ComponentCheck>{new ComponentCheck{Name="fixture prerequisites",Present=true}},
            NativeServices=new List<NativeService>{new NativeService{Name="Corsair fixture service",Role="Corsair",Trusted=true,StartMode="Auto",State="Running"},new NativeService{Name=brand+" fixture light",Role="Lighting",Trusted=true,StartMode="Auto",State="Running"}} };
    }
    static Enrollment Enroll(Snapshot s) { return new Enrollment{Schema=2,Brand=s.Brand,Enabled=true,OwnerSid=StateStore.Sid,Fingerprint=s.Fingerprint,NativeConfirmedUtc="2000-01-01T01:00:00Z",BaselineServices=s.NativeServices.Select(v=>v.Name).ToList()}; }
    static Snapshot GccConnection(int pid,bool connected) {
        string start=pid==42?"2000-01-01T01:01:00Z":"2000-01-01T01:02:00Z";
        return new Snapshot {
            Brand="GIGABYTE",BoardModel="B550 VISION D-P",BoardVendor="GIGABYTE",OsVersion="10.0.28000.1",
            GccVersion="26.03.26.01",StorageVersion="26.04.27.02",DcsVersion="1.9.15",IcueVersion="5.46.67",
            Fingerprint="fixture-gcc",BootUtc="2000-01-01T01:00:00Z",GccPid=pid,GccStartUtc=pid==0?null:start,
            GccPath=Path.Combine(Probe.GccDir,"GCC.exe"),DcsStart=3,DcsState="Running",
            Ram=new List<RamInfo>{new RamInfo{Vendor="Corsair",Part="CMT32GX4M2D3600C18"},new RamInfo{Vendor="Corsair",Part="CMT32GX4M2D3600C18"}},
            IcueDrivers=new List<DriverInfo>{new DriverInfo{Name="CorsairLLAccess"+new string('A',40),Path=Path.Combine(Probe.IcueDir,"CorsairLLAccess64.sys"),Start=3,State="Stopped"}},
            Evidence=new LogEvidence{CurrentSession=pid>0,Connected=connected,Modules=2,SessionUtc=start}
        };
    }
    static void GigabyteTests() {
        var ready=GccConnection(42,true);
        Assert(GigabyteRecovery.CanConnect(ready) && GigabyteRecovery.IsConnected(ready),"known Gigabyte environment exposes manual connection and strict healthy evidence");
        Rules.Evaluate(ready);
        Assert(!ready.RuleEligible && !Rules.CanAutoRepair(ready,new Enrollment{Enabled=true,Fingerprint=ready.Fingerprint},DateTime.UtcNow),"manual Gigabyte support cannot enable persistent or automatic recovery");
        Assert(UiState.For(ready).Tone=="success" && UiState.For(ready).PrimaryAction=="repair","strict Gigabyte connection evidence drives healthy manual action presentation");
        var environment=new FakeGigabyteConnection();
        string result=GigabyteRecovery.Run(ready,environment);
        Assert(result.Contains("无需重新连接") && environment.Restarts==0 && environment.Reads==0 && environment.Verifications==0 && environment.Waits==0,"already connected manual request returns without process or service work");
        var sample=GccConnection(42,true); sample.GccStartUtc=null; sample.Health="接管链路正常";
        Assert(!GigabyteRecovery.IsConnected(sample) && UiState.For(sample).Tone!="success","health text without a process creation time cannot claim connection success");
        sample=GccConnection(42,true); sample.Evidence.SessionUtc="1999-12-31T01:00:00Z";
        Assert(!GigabyteRecovery.IsConnected(sample),"old session timestamp cannot confirm current connection");
        sample=GccConnection(42,true); sample.Errors.Add("fixture read failure");
        Assert(!GigabyteRecovery.IsConnected(sample) && !GigabyteRecovery.CanConnect(sample),"scan errors block success and manual mutation");
        sample=GccConnection(42,true); sample.Evidence.Modules=1;
        Assert(!GigabyteRecovery.IsConnected(sample),"partial memory enumeration cannot confirm connection");
        sample=GccConnection(42,true); sample.Evidence.PipeErrors=1;
        Assert(!GigabyteRecovery.IsConnected(sample),"communication error blocks connected status");
        sample=GccConnection(42,true); sample.Evidence.InstalledWriteFailures=1;
        Assert(!GigabyteRecovery.IsConnected(sample),"installed memory write error blocks connected status");
        sample=GccConnection(42,true); sample.DcsState="Stopped";
        Assert(!GigabyteRecovery.IsConnected(sample),"connected log with a stopped service is insufficient");
        sample=GccConnection(42,false); sample.BoardModel="Other board";
        Assert(!GigabyteRecovery.CanConnect(sample) && GigabyteRecovery.ConnectionBlockReason(sample).Contains("B550 VISION D-P"),"manual connection keeps the explicitly supported board scope");
        sample=GccConnection(42,false); sample.GccVersion="other";
        Assert(!GigabyteRecovery.CanConnect(sample),"unverified software version blocks manual connection");
        sample=GccConnection(42,false); sample.IcueRunning=true;
        Assert(!GigabyteRecovery.CanConnect(sample),"active iCUE blocks manual driver handoff");
        sample=GccConnection(42,false); sample.OtherCorsairUsb=true;
        Assert(!GigabyteRecovery.CanConnect(sample),"other Corsair USB devices block manual driver handoff");
        sample=GccConnection(42,false); sample.IcueDrivers[0].Start=4;
        Assert(!GigabyteRecovery.CanConnect(sample),"disabled driver is not enabled by manual connection");

        var initial=GccConnection(42,false);
        environment=new FakeGigabyteConnection(); environment.Observations.Enqueue(GccConnection(43,true));
        result=GigabyteRecovery.Run(initial,environment);
        Assert(environment.Restarts==1 && environment.Waits==0 && environment.Verifications==1 && result.Contains("已确认"),"new session confirmed immediately avoids the old fixed wait");
        environment=new FakeGigabyteConnection(); environment.Observations.Enqueue(GccConnection(0,false)); environment.Observations.Enqueue(GccConnection(43,true));
        GigabyteRecovery.Run(initial,environment);
        Assert(environment.Waits==1 && environment.Elapsed==TimeSpan.FromMilliseconds(500),"pending process is polled once and success ends waiting early");
        environment=new FakeGigabyteConnection{Current=GccConnection(43,false)};
        bool failed=false;
        try { GigabyteRecovery.Run(initial,environment); } catch(System.TimeoutException) { failed=true; }
        Assert(failed && environment.Restarts==1 && environment.Elapsed==TimeSpan.FromSeconds(38),"connection timeout is bounded with no blind restart retry");
        environment=new FakeGigabyteConnection{Current=GccConnection(42,true)};
        failed=false; try { GigabyteRecovery.Run(initial,environment); } catch(InvalidOperationException) { failed=true; }
        Assert(failed && environment.Verifications==0,"old GCC process cannot satisfy a new connection operation");
        environment=new FakeGigabyteConnection(); environment.Observations.Enqueue(GccConnection(43,false)); environment.Observations.Enqueue(GccConnection(44,true));
        failed=false; try { GigabyteRecovery.Run(initial,environment); } catch(InvalidOperationException) { failed=true; }
        Assert(failed && environment.Verifications==0,"process replacement during connection is surfaced instead of claiming success");
        sample=GccConnection(43,false); sample.Evidence.PipeErrors=1;
        environment=new FakeGigabyteConnection{Current=sample};
        failed=false; try { GigabyteRecovery.Run(initial,environment); } catch(InvalidOperationException) { failed=true; }
        Assert(failed && environment.Waits==0,"fresh communication error terminates the connection wait");
        sample=GccConnection(43,true); sample.Errors.Add("fixture log read failure");
        environment=new FakeGigabyteConnection{Current=sample};
        failed=false; try { GigabyteRecovery.Run(initial,environment); } catch(InvalidOperationException) { failed=true; }
        Assert(failed && environment.Verifications==0,"fresh read failure cannot return connection success");
        sample=GccConnection(43,true); sample.Fingerprint="changed";
        environment=new FakeGigabyteConnection{Current=GccConnection(43,true),Final=sample};
        failed=false; try { GigabyteRecovery.Run(initial,environment); } catch(InvalidOperationException) { failed=true; }
        Assert(failed && environment.Verifications==1,"final full verification rejects component fingerprint drift");
        sample=GccConnection(42,false); sample.Evidence.PipeErrors=1;
        GigabyteRecovery.CheckEnvironment(sample);
        environment=new FakeGigabyteConnection{Current=GccConnection(43,true)};
        GigabyteRecovery.Run(sample,environment);
        Assert(environment.Restarts==1,"existing session errors remain eligible for a fresh recovery attempt");
        bool touched=false; int requests=0;
        failed=false;
        try { WindowsGigabyteConnection.StopAndWait(()=>requests++,()=>{ throw new System.TimeoutException("fixture stop wait"); },ref touched); }
        catch(System.TimeoutException) { failed=true; }
        Assert(failed && touched && requests==1,"accepted stop remains recorded when status waiting fails so rollback includes it");
        DateTime boot=DateTime.Parse("2000-01-01T01:00:00Z").ToUniversalTime();
        string connected=Session(42,"");
        var rotated=LogParser.Parse(new[]{connected,"2000-01-01 01:01:04 W svc.server.json: Error writing reply to client \"GCC.exe\" ( \"\\\\.\\pipe\\:pid:42\\{test}\" )"},42,boot,boot);
        Assert(rotated.PipeErrors==1,"new errors in a rotated log cannot fall back to an older healthy file");
        rotated=LogParser.Parse(new[]{connected,"2000-01-01 01:01:05 I svc.server: New client connected: \"iCUE.exe\""},42,boot,boot);
        Assert(!rotated.CurrentSession,"new controller in a rotated log invalidates earlier GCC ownership");
        sample=GccConnection(43,false); sample.Evidence.CurrentSession=false; sample.Evidence.Note="出现其他客户端，归属不明确";
        environment=new FakeGigabyteConnection{Current=sample};
        failed=false; try { GigabyteRecovery.Run(initial,environment); } catch(InvalidOperationException) { failed=true; }
        Assert(failed && environment.Waits==0,"new controller ownership ends waiting immediately without reporting success");
    }
    static void RecoveryTests() {
        Assert(WindowsServiceStarter.ServicePath("VendorService")=="Win32_Service.Name=\"VendorService\"","recovery lookup targets exactly one named service");
        string specialName="Vendor'\"\\Instance";
        string path=WindowsServiceStarter.ServicePath(specialName);
        Assert(path=="Win32_Service.Name=\"Vendor'\\\"\\\\Instance\"","service object path preserves apostrophe and escapes quotes and backslashes");
        var parsed=new ManagementPath(path);
        Assert(parsed.IsInstance && parsed.ClassName=="Win32_Service","escaped recovery lookup remains a single service instance path");
        var expected=new NativeService{Name="VendorService",Command="\"C:\\Program Files\\Vendor\\Service.exe\" /service",StartMode="Auto",Account="LocalSystem"};
        var actual=new NativeService{Name=expected.Name,Command=expected.Command,StartMode="Auto",Account=expected.Account};
        Assert(WindowsServiceStarter.ConfigurationMatches(expected,actual),"unchanged official automatic service passes execution-time configuration check");
        Assert(!WindowsServiceStarter.ConfigurationMatches(expected,null),"service removed before recovery fails its configuration check");
        actual.Command=expected.Command+" /changed";
        Assert(!WindowsServiceStarter.ConfigurationMatches(expected,actual),"service command drift blocks recovery"); actual.Command=expected.Command;
        actual.Account="OtherAccount";
        Assert(!WindowsServiceStarter.ConfigurationMatches(expected,actual),"service account drift blocks recovery"); actual.Account=expected.Account;
        actual.StartMode="Manual";
        Assert(!WindowsServiceStarter.ConfigurationMatches(expected,actual),"manual startup cannot reuse the automatic service baseline"); actual.StartMode="Auto";
        actual.Name="OtherService";
        Assert(!WindowsServiceStarter.ConfigurationMatches(expected,actual),"another service cannot satisfy the targeted recovery lookup");
        var snapshot=Native("ASUS"); Rules.Evaluate(snapshot);
        var starter=new FakeServices();
        NativeRecovery.Run(snapshot,Enroll(snapshot),starter);
        Assert(starter.Started.Count==0,"already running baseline services cause no recovery calls or service waits");
    }
    static void ProbeTests() {
        var expected = new Dictionary<ServiceControllerStatus,string> {
            {ServiceControllerStatus.Stopped,"Stopped"}, {ServiceControllerStatus.StartPending,"Start Pending"},
            {ServiceControllerStatus.StopPending,"Stop Pending"}, {ServiceControllerStatus.Running,"Running"},
            {ServiceControllerStatus.ContinuePending,"Continue Pending"}, {ServiceControllerStatus.PausePending,"Pause Pending"},
            {ServiceControllerStatus.Paused,"Paused"}
        };
        foreach(var value in expected) Assert(ServiceProbe.State(value.Key) == value.Value,"SCM state preserves " + value.Value);
        bool invalidState=false;
        try { ServiceProbe.State((ServiceControllerStatus)0); } catch(InvalidOperationException) { invalidState=true; }
        Assert(invalidState,"invalid SCM state fails instead of inventing a stopped service");
        var missingDcs=new Snapshot();
        ServiceProbe.ReadDcs(missingDcs,new ServiceController[0]);
        Assert(missingDcs.DcsState==null && missingDcs.DcsStart==-1,"missing DCS stays absent with unknown start mode instead of a boot-start value");
        string driver=Path.Combine(Probe.IcueDir,"CorsairLLAccess64.sys"), name="CorsairLLAccess"+new string('A',40);
        Assert(ServiceProbe.IsIcueDriver(name,driver),"installed iCUE driver matches its exact name and path");
        Assert(ServiceProbe.IsIcueDriver(name,driver.ToUpperInvariant()),"driver path comparison remains case insensitive");
        Assert(!ServiceProbe.IsIcueDriver(name+"A",driver) && !ServiceProbe.IsIcueDriver("CorsairLLAccessOther",driver),"unrecognized driver service names are excluded");
        Assert(!ServiceProbe.IsIcueDriver(name,Path.Combine(Path.GetTempPath(),"CorsairLLAccess64.sys")),"same driver filename outside the installed iCUE directory is excluded");
        const string executable=@"C:\Program Files\GIGABYTE\Control Center\GCC.exe";
        var scheduler=new FakeTaskScheduler();
        scheduler.Task.Value.Actions.Values.Add(new FakeTaskAction{Type=0,Path="\""+executable.ToUpperInvariant()+"\""});
        Assert(TaskControl.ReadGccStartup(scheduler,executable),"GCC startup read preserves quoted and case-insensitive executable matching");
        scheduler.Task.Enabled=false;
        int reads=scheduler.Task.DefinitionReads;
        Assert(!TaskControl.ReadGccStartup(scheduler,executable) && scheduler.Task.DefinitionReads==reads,"disabled GCC task never reads or accepts its actions");
        scheduler.Task.Enabled=true;
        scheduler.Task.Value.Actions.Values.Insert(0,new FakeTaskAction{Type=5,Path=null});
        Assert(TaskControl.ReadGccStartup(scheduler,executable),"non-executable task action is ignored before reading its path");
        scheduler.Task.Value.Actions.Values[1].Path=executable+".other";
        Assert(!TaskControl.ReadGccStartup(scheduler,executable),"a different executable cannot satisfy the GCC startup check");
        scheduler.Task.Value.Actions.Values.Clear();
        Assert(!TaskControl.ReadGccStartup(scheduler,executable),"task without execution actions is not reported as autostart");
        scheduler.Failure=new InvalidOperationException("simulated task query failure");
        bool failed=false;
        try { TaskControl.ReadGccStartup(scheduler,executable); } catch(TargetInvocationException ex) { failed=ex.InnerException is InvalidOperationException; }
        Assert(failed,"task query failure remains an error at the read boundary");
        scheduler.Failure=null;
        scheduler.TaskFailure=new COMException("simulated missing task",unchecked((int)0x80070002));
        Assert(!TaskControl.ReadGccStartup(scheduler,executable),"GetTask file-not-found is a legitimate absent task");
        scheduler.TaskFailure=new COMException("simulated missing account information",unchecked((int)0x8004130F));
        failed=false;
        try { TaskControl.ReadGccStartup(scheduler,executable); } catch(TargetInvocationException ex) { failed=(uint)ex.InnerException.HResult==0x8004130F; }
        Assert(failed,"task account configuration failure cannot be reported as a missing task");
        scheduler.TaskFailure=null;
        scheduler.Failure=new COMException("simulated connection file-not-found",unchecked((int)0x80070002));
        failed=false;
        try { TaskControl.ReadGccStartup(scheduler,executable); } catch(TargetInvocationException ex) { failed=(uint)ex.InnerException.HResult==0x80070002; }
        Assert(failed,"file-not-found while connecting is distinct from a missing GCC task");
        scheduler.Failure=new COMException("simulated access denied",unchecked((int)0x80070005));
        failed=false;
        try { TaskControl.ReadGccStartup(scheduler,executable); } catch(TargetInvocationException ex) { failed=(uint)ex.InnerException.HResult==0x80070005; }
        Assert(failed,"task access denial cannot be reported as a missing task");
    }
    static void NativeTests(DateTime now) {
        foreach(string brand in new[]{"ASUS","MSI"}) {
            var s=Native(brand); Rules.Evaluate(s); var e=Enroll(s);
            Assert(s.RuleEligible && NativePolicy.CanEnroll(s),brand+": prerequisites enable baseline enrollment with iCUE running");
            Assert(s.Health!="接管链路正常",brand+": running services do not claim RGB ownership");
            Assert(!Rules.CanAutoRepair(s,e,now),brand+": healthy services trigger no operation");
            s.NativeServices[1].State="Stopped"; Rules.Evaluate(s);
            Assert(s.RuleEligible&&!NativePolicy.CanEnroll(s),brand+": stopped service cannot establish first successful baseline");
            Assert(Rules.CanAutoRepair(s,e,now),brand+": existing baseline restores stopped native service without blocking iCUE");
            var fake=new FakeServices(); NativeRecovery.Run(s,e,fake);
            Assert(fake.Started.SequenceEqual(new[]{brand+" fixture light"}),brand+": only stopped baseline service receives a start");
            s.NativeServices[0].State="Stopped"; fake=new FakeServices(); NativeRecovery.Run(s,e,fake);
            Assert(fake.Started.SequenceEqual(e.BaselineServices),brand+": dependency order is retained");
            fake=new FakeServices{Fail=true}; bool failed=false; try { NativeRecovery.Run(s,e,fake); } catch(InvalidOperationException) { failed=true; }
            Assert(failed&&fake.Started.Count==0,brand+": failed service start stops recovery without fabricated success");
            e.NativeConfirmedUtc=null; Assert(!Rules.CanAutoRepair(s,e,now),brand+": no user-confirmed handoff blocks recovery"); e.NativeConfirmedUtc="confirmed";
            e.BaselineServices.Clear(); Assert(!Rules.CanAutoRepair(s,e,now),brand+": service outside baseline is never started"); e=Enroll(s);
            e.Fingerprint="changed"; Assert(!Rules.CanAutoRepair(s,e,now),brand+": component or service configuration drift suspends recovery"); e.Fingerprint=s.Fingerprint;
            e.LastRepairUtc=now.AddMinutes(-5).ToString("o"); Assert(!Rules.CanAutoRepair(s,e,now),brand+": recovery cooldown applies"); e.LastRepairUtc=null;
            e.RepairDate=now.ToString("yyyy-MM-dd"); e.RepairsToday=3; Assert(!Rules.CanAutoRepair(s,e,now),brand+": daily cap applies");
            s.NativeServices[1].StartMode="Manual"; Rules.Evaluate(s); Assert(!s.RuleEligible,brand+": manual service startup is not rewritten"); s.NativeServices[1].StartMode="Disabled"; Rules.Evaluate(s); Assert(!s.RuleEligible,brand+": disabled service is not enabled"); s.NativeServices[1].StartMode="Auto";
            s.NativeServices[1].Trusted=false; Rules.Evaluate(s); Assert(!s.RuleEligible,brand+": unknown signer is diagnostic only"); s.NativeServices[1].Trusted=true;
            s.NativeServices[1].State="Start Pending"; Rules.Evaluate(s); Assert(!s.RuleEligible,brand+": pending state blocks repair"); s.NativeServices[1].State="Running";
            s.Requirements[0].Present=false; Rules.Evaluate(s); Assert(!s.RuleEligible,brand+": missing plugin or native app blocks enrollment");
            Assert(!NativePolicy.Plan(s).Contains("底层驱动改为按需启动"),brand+": native plan excludes Gigabyte driver policy");
            Assert(Enroll(s).Changes.Count==0,brand+": native baseline carries no registry mutations");
            var json=Json.Write(e); var roundtrip=Json.Read<Enrollment>(json); Assert(roundtrip.Schema==2 && roundtrip.Brand==brand && roundtrip.BaselineServices.SequenceEqual(e.BaselineServices),brand+": protected baseline survives serialization");
        }
        Assert(NativeProbe.Executable("\"C:\\Program Files\\Vendor\\service.exe\" /service")==@"C:\Program Files\Vendor\service.exe","quoted service paths are parsed without executing arguments");
        Assert(NativeProbe.Executable(@"C:\Program Files\Vendor\service.exe /service")==null,"ambiguous unquoted service path is rejected");
        Assert(NativeProbe.Executable(@"C:\Vendor\service.exe -service")==@"C:\Vendor\service.exe","unquoted service path without spaces is supported");
        Assert(!NativeProbe.PublisherMatches("Some Vendor","ASUS") && NativeProbe.PublisherMatches("ASUSTeK COMPUTER INC.","ASUS"),"ASUS publisher is checked");
        Assert(NativeProbe.PublisherMatches("MICRO-STAR INTERNATIONAL CO., LTD.","MSI") && !NativeProbe.PublisherMatches("ASUSTeK COMPUTER INC.","MSI"),"MSI publisher cannot use ASUS signature");
        Assert(NativeProbe.Role("ASUS","MSI_Central_Service.exe")==null && NativeProbe.Role("MSI","LightingService.exe")==null,"vendor services cannot cross adapters");
        Assert(!NativeProbe.ProtectedLocation(Path.Combine(Path.GetTempPath(),"missing.exe")),"nonexistent and non-installation paths are rejected");
        var old=Json.Read<Enrollment>("{\"Schema\":1,\"Enabled\":false,\"Changes\":[]}"); Assert(old.Schema==1&&old.BaselineServices.Count==0,"v0.1 enrollment can be loaded without invented native confirmation");
        var inventory=new Snapshot();
        inventory.Apps.Add(new AppInfo{Name="CORSAIR RGB Memory Plugin for ASUS AURA SYNC"}); Assert(NativeProbe.HasAuraPlugin(inventory),"official ASUS plugin display name is recognized");
        inventory.Apps[0].Name="Corsair Plugin for Asus Aura Sync"; Assert(NativeProbe.HasAuraPlugin(inventory),"legacy ASUS plugin display name is recognized");
        inventory.Apps[0].Name="Corsair iCUE5 Software"; Assert(!NativeProbe.HasAuraPlugin(inventory),"iCUE installation alone is not an ASUS memory plugin");
        inventory.Apps[0].Name="MSI Center SDK"; Assert(NativeProbe.HasMsiCenter(inventory),"MSI Center SDK installation is recognized");
        inventory.Apps[0].Name="MSI Center"; Assert(NativeProbe.HasMsiCenter(inventory),"MSI Center installation is recognized");
        inventory.Apps[0].Name="MSI Center S"; Assert(!NativeProbe.HasMsiCenter(inventory),"MSI Center S is not silently treated as MSI Center");
        inventory.Apps[0].Name="MSI Dragon Center"; Assert(!NativeProbe.HasMsiCenter(inventory),"Dragon Center cannot reuse the MSI Center adapter");
        var missingRam=Native("ASUS"); missingRam.Ram.Clear(); Rules.Evaluate(missingRam); Assert(!missingRam.RuleEligible,"native adapters need a Corsair RAM candidate");
    }
    public static int Main(string[] args) {
        try {
            bool rulesOnly=args.Length==3 && args[0]=="--rules-tests";
            if(rulesOnly) args=args.Skip(1).ToArray();
            if(args.Length==1 && args[0]=="--gigabyte-tests") { GigabyteTests(); Console.WriteLine("Passed "+count+" Gigabyte checks."); return 0; }
            if(args.Length==1 && args[0]=="--recovery-tests") { RecoveryTests(); Console.WriteLine("Passed "+count+" recovery checks."); return 0; }
            if(!rulesOnly) ProbeTests();
            if(args.Length==1 && args[0]=="--probe-tests") { Console.WriteLine("Passed "+count+" probe checks."); return 0; }
            if(!rulesOnly) { RecoveryTests(); GigabyteTests(); }
            DateTime boot=DateTime.Parse("2000-01-01T01:00:00Z").ToUniversalTime();
            NativeTests(boot.AddMinutes(3));
            Assert(UiState.For(null).PrimaryAction=="none","loading screen cannot suggest a system mutation");
            var screen=Native("ASUS"); Rules.Evaluate(screen);
            Assert(UiState.For(screen).PrimaryAction=="enable" && UiState.For(screen).Tone!="success","ASUS readiness offers confirmed enrollment without claiming RGB success");
            screen.NativeServices[1].State="Stopped"; Rules.Evaluate(screen);
            Assert(UiState.For(screen).PrimaryAction=="help","first enrollment cannot bypass a stopped native service");
            screen.NativeConfirmed=true; screen.AssistantEnabled=true;
            Assert(UiState.For(screen).PrimaryAction=="repair","confirmed native baseline offers service recovery");
            screen.AssistantState="版本变化，自动恢复暂停";
            Assert(UiState.For(screen).PrimaryAction=="details","changed environment leads to inspection instead of repair");
            screen.Errors.Add("fixture read failure");
            Assert(UiState.For(screen).PrimaryAction=="scan","incomplete scan offers retry instead of mutation");
            screen=GccSample(); screen.Health="接管链路正常"; Rules.Evaluate(screen);
            Assert(!screen.RuleEligible && UiState.For(screen).PrimaryAction=="help","GCC remains diagnostic only");
            screen.Health="发现当前连接异常";
            Assert(UiState.For(screen).PrimaryAction=="help","GCC errors direct to official setup");
            screen.RuleEligible=true;
            Assert(UiState.For(screen).PrimaryAction=="help","legacy eligibility cannot expose a GCC mutation action");
            Assert(Operations.Plan(screen).Contains("仅提供检测"),"GCC plan never offers startup changes");
            string good=Session(42,""); var e=LogParser.Parse(good,42,boot,boot);
            Assert(e.CurrentSession&&e.Modules==2&&e.Connected,"current GCC session and two DIMMs are detected");
            Assert(e.InstalledWriteFailures==0,"empty-slot writes do not trigger repair");
            Assert(!LogParser.Parse(good,43,boot,boot).CurrentSession,"old GCC PID is ignored");
            Assert(!LogParser.Parse(good,42,boot.AddDays(1),boot).CurrentSession,"previous boot logs are ignored");
            Assert(!LogParser.Parse(good,42,boot,boot.AddMinutes(5)).CurrentSession,"reused PID with a newer process start is ignored");
            string bad=Session(42,"2000-01-01 01:01:01 W svc.lla: Byte write failed: dram=[1:1] offset=SMBSpeed\n")+"2000-01-01 01:01:03 W svc.server.json: Error writing reply to client \"GCC.exe\" ( \"\\\\.\\pipe\\:pid:42\\{test}\" )\n2000-01-01 01:01:04 I svc: Stopping service: \"CorsairDeviceControlService\"\n";
            e=LogParser.Parse(bad,42,boot,boot); Assert(e.InstalledWriteFailures==1&&e.PipeErrors==1&&!e.Connected,"installed-module failure and closed pipe are identified");
            Assert(!LogParser.Parse(good+"2000-01-01 01:01:05 I svc.server: New client connected: \"iCUE.exe\"\n",42,boot,boot).CurrentSession,"ambiguous ownership does not trigger automatic recovery");
            var s=GccSample(); s.Evidence=LogParser.Parse(good,42,boot,boot); Rules.Evaluate(s);
            Assert(!s.RuleEligible && s.Health=="接管链路正常","GCC connection evidence is visible without repair eligibility");
            s.DcsState="Stopped"; s.Evidence=new LogEvidence(); Rules.Evaluate(s);
            Assert(s.Health!="发现当前连接异常","stopped service without session evidence is not a fault");
            s=GccSample(); s.Evidence=LogParser.Parse(bad,42,boot,boot); Rules.Evaluate(s);
            var enrollment=new Enrollment{Enabled=true,Fingerprint="sample"};
            Assert(!Rules.CanAutoRepair(s,enrollment,boot.AddMinutes(3)),"GCC errors never trigger automatic changes");
            s.RuleEligible=true;
            Assert(!Rules.CanAutoRepair(s,enrollment,boot.AddMinutes(3)),"legacy GCC enrollment cannot resume recovery");
            s.Brand="Other";
            Assert(!Rules.CanAutoRepair(s,enrollment,boot.AddMinutes(3)),"unsupported brands cannot use legacy recovery");
            var before=new RegistryValue{Key="test",Name="Start",Exists=true,Kind="DWord",Value="2"}; var applied=new RegistryValue{Key="test",Name="Start",Exists=true,Kind="DWord",Value="3"}; var change=new RegistryChange{Before=before,Applied=applied};
            var store=new FakeStore{Value=applied}; Assert(Changes.Restore(store,change)&&store.Value.Value=="2","rollback restores the exact prior value");
            Assert(Changes.Restore(store,change),"repeated rollback is idempotent");
            store.Value=new RegistryValue{Key="test",Name="Start",Exists=true,Kind="DWord",Value="4"}; Assert(!Changes.Restore(store,change)&&store.Value.Value=="4","rollback preserves external edits");
            var missing=new RegistryValue{Key="test",Name="missing",Exists=false}; store.Value=applied; Assert(Changes.Restore(store,new RegistryChange{Before=missing,Applied=applied})&&!store.Value.Exists,"rollback preserves original value absence");
            if(args.Length!=2) throw new InvalidOperationException("Both synthetic fixtures are required.");
            var fixtureGood=LogParser.Parse(File.ReadAllText(args[0]),42,boot,boot);
            Assert(fixtureGood.CurrentSession&&fixtureGood.Modules==2&&fixtureGood.Connected&&fixtureGood.InstalledWriteFailures==0,"synthetic connected fixture parses correctly");
            var fixtureBad=LogParser.Parse(File.ReadAllText(args[1]),42,boot,boot);
            Assert(fixtureBad.CurrentSession&&fixtureBad.Modules==2&&fixtureBad.InstalledWriteFailures==2&&fixtureBad.PipeErrors==1&&!fixtureBad.Connected,"synthetic disconnected fixture parses correctly");
            Console.WriteLine("Passed "+count+" checks."); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
