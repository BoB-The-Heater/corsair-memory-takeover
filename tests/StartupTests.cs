using System;
using System.IO;
using CorsairTakeover;

internal sealed class FakeStartupBackend : IStartupBackend {
    public string Sid { get { return "S-1-5-21-100-200-300-1001"; } }
    public string Root { get { return @"C:\Protected\CorsairTakeoverAssistant"; } }
    public string Executable=@"C:\Downloads\CorsairTakeover.exe";
    public string CurrentExe { get { return Executable; } }
    public StartupEnrollment State;
    public StartupTask Task;
    public OperationResult Result;
    public Snapshot Current = StartupTests.Sample();
    public int Connects, Registers, Removes, Saves, Scans;
    public bool CorruptHash, FailRegister, FailRemove, FailConnect, FailRead, FailResult, FailEnabledSave, CorruptTask, MissingInstalled, FailResultRead;
    public void Prepare() { }
    public StartupEnrollment Load() { return State==null?null:Json.Read<StartupEnrollment>(Json.Write(State)); }
    public void Save(StartupEnrollment value) {
        Saves++;
        if(FailEnabledSave && value.Enabled) throw new IOException("synthetic enabled save failure");
        State=Json.Read<StartupEnrollment>(Json.Write(value));
    }
    public string Hash(string path) { return CorruptHash?new string('B',64):new string('A',64); }
    public void VerifyFile(string path) {
        if(FailRead) throw new IOException("synthetic protected file failure");
        if(MissingInstalled) throw new FileNotFoundException("synthetic missing installed executable");
    }
    public void Install(string source,string destination,string hash) { }
    public Snapshot Scan() { Scans++; if(FailRead) throw new IOException("synthetic scan failure"); return Current; }
    public string Connect(Snapshot snapshot) { Connects++; if(FailConnect) throw new IOException("synthetic connection failure"); return "synthetic connection confirmed"; }
    public StartupTask ReadTask() { if(FailRead) throw new IOException("synthetic task read failure"); return Task; }
    public void RegisterTask(StartupEnrollment state) {
        Registers++;
        if(FailRegister) throw new IOException("synthetic registration failure");
        Task=StartupConnection.ExpectedTask(state,Sid,Root);
        if(CorruptTask) Task.Arguments="--watch";
    }
    public void RemoveTask() { Removes++; if(FailRemove) throw new IOException("synthetic delete failure"); Task=null; }
    public OperationResult ReadResult() { if(FailResultRead) throw new IOException("synthetic damaged or unreadable result"); return Result; }
    public void SaveResult(OperationResult value) { if(FailResult) throw new IOException("synthetic result save failure"); Result=value; }
}

public static class StartupTests {
    static int checks;
    static void Assert(bool condition,string message) { if(!condition) throw new Exception("FAIL: "+message); checks++; Console.WriteLine("PASS: "+message); }
    static void Reject(Action action,string message) {
        bool rejected=false;
        try { action(); } catch(InvalidOperationException) { rejected=true; } catch(IOException) { rejected=true; } catch(AggregateException) { rejected=true; }
        Assert(rejected,message);
    }
    internal static Snapshot Sample() {
        var snapshot=new Snapshot { Brand="GIGABYTE",BoardModel="B550 VISION D-P",Fingerprint=new string('1',64),OsVersion="10.0.28000.1",
            GccPath=Path.Combine(Probe.GccDir,"GCC.exe"),GccVersion="26.03.26.01",StorageVersion="26.04.27.02",DcsVersion="1.9.15",IcueVersion="5.46.67",DcsStart=2,DcsState="Running" };
        snapshot.Ram.Add(new RamInfo { Vendor="Corsair",Part="CMT32GX4M2D3600C18" });
        snapshot.Ram.Add(new RamInfo { Vendor="Corsair",Part="CMT32GX4M2D3600C18" });
        snapshot.IcueDrivers.Add(new DriverInfo { Name="CorsairLLAccess"+new string('A',40),Path=Path.Combine(Probe.IcueDir,"CorsairLLAccess64.sys"),Start=3,State="Stopped" });
        return snapshot;
    }
    static FakeStartupBackend Enabled() {
        var backend=new FakeStartupBackend();
        StartupConnection.Enable(backend.Current.Fingerprint,backend);
        backend.Executable=backend.State.InstalledExe;
        return backend;
    }
    public static int Main() {
        try { Run(); return 0; } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    public static void Run() {
        string initiatingSid="S-1-5-21-100-200-300-1001";
        Assert(Program.StartupIdentityMatches(initiatingSid,initiatingSid),"startup settings accept the same canonical initiating and process identity");
        Assert(!Program.StartupIdentityMatches(initiatingSid,"S-1-5-18"),"startup settings reject elevation under a different account");
        Assert(!Program.StartupIdentityMatches("BA",initiatingSid) && !Program.StartupIdentityMatches("invalid",initiatingSid) && !Program.StartupIdentityMatches(null,initiatingSid),"startup settings reject SID aliases, malformed SID and missing SID");
        Assert(!Program.StartupIdentityMatches("S-1-5-999999999999999999999999",initiatingSid),"startup settings reject out-of-range SID values");
        Assert(Program.Main(new[]{"--startup-enable-confirmed",new string('1',64),"invalid"})==4,"invalid initiating SID exits before administrator checks and writes");
        string foreignSid=StateStore.Sid=="S-1-5-18"?"S-1-5-19":"S-1-5-18";
        Assert(Program.Main(new[]{"--startup-enable-confirmed",new string('1',64),foreignSid})==4,"different initiating SID prevents enable before configuration access");
        Assert(Program.Main(new[]{"--startup-disable",foreignSid})==4,"different initiating SID prevents disable before configuration access");
        Assert(Program.Main(new[]{"--startup-disable","BA"})==4,"invalid initiating SID prevents disable before configuration access");
        Assert(Program.Main(new[]{"--startup-enable-confirmed",new string('1',64)})==2 && Program.Main(new[]{"--startup-disable"})==2,"legacy startup setting arguments without identity cannot mutate configuration");
        var fresh=new FakeStartupBackend();
        var empty=StartupConnection.ReadStatus(fresh);
        Assert(!empty.Enabled&&!empty.CanDisable,"absent authorization and task are disabled");
        var missingFolder=new FakeMissingStartupFolder { Failure=new FileNotFoundException("synthetic missing native task") };
        Assert(WindowsStartupBackend.FindTask(missingFolder,"synthetic")==null,"native task not-found mapped to FileNotFoundException is absent");
        missingFolder.Failure=new System.Runtime.InteropServices.COMException("synthetic missing COM task",unchecked((int)0x80070002));
        Assert(WindowsStartupBackend.FindTask(missingFolder,"synthetic")==null,"native COM task not-found is absent");
        missingFolder.Failure=new IOException("synthetic other task read failure");
        Reject(()=>WindowsStartupBackend.FindTask(missingFolder,"synthetic"),"other native task read errors remain failures");
        Reject(()=>StartupConnection.Enable("bad",fresh),"invalid confirmation rejected before registration");
        Assert(fresh.Registers==0,"invalid confirmation has no task mutation");
        Reject(()=>StartupConnection.Enable(new string('2',64),fresh),"stale confirmed component fingerprint rejected");
        var backend=Enabled();
        Assert(backend.State.Enabled && backend.Registers==1 && backend.Connects==0,"enable registers once without connecting");
        var status=StartupConnection.ReadStatus(backend);
        Assert(status.Enabled && status.CanDisable,"enabled requires matching task and authorization readback");
        Assert(WindowsStartupBackend.ParseState(Json.Write(backend.State)).Enabled,"persisted authorization round-trips required typed fields");
        Reject(()=>WindowsStartupBackend.ParseState("{\"Schema\":1}"),"missing Enabled is invalid instead of default false");
        Reject(()=>WindowsStartupBackend.ParseState(Json.Write(backend.State).Replace("\"Enabled\":true","\"Enabled\":\"true\"")),"string boolean cannot silently grant authorization");
        Reject(()=>WindowsStartupBackend.ParseResult("{\"Message\":\"missing result fields\"}"),"incomplete result is invalid instead of default failure");
        var savedResult=new OperationResult { Success=false,Message="synthetic failure",AtUtc=DateTime.UtcNow.ToString("o") };
        Assert(!WindowsStartupBackend.ParseResult(Json.Write(savedResult)).Success,"explicit failure result round-trips without conversion");
        Reject(()=>StartupConnection.Enable(backend.Current.Fingerprint,backend),"enabled baseline cannot be overwritten");
        Assert(backend.Registers==1,"duplicate enable preserves original task");
        var task=backend.Task;
        Assert(task.Source==StartupConnection.Marker && task.OwnerSid==backend.Sid,"dedicated ownership marker and current user principal");
        Assert(task.LogonType==3 && task.RunLevel==1 && task.TriggerType==9 && task.TriggerSid==backend.Sid,"interactive highest privilege current-user logon only");
        Assert(task.Delay=="PT25S" && task.ExecutionTimeLimit=="PT5M" && task.MultipleInstances==2 && task.RestartCount==0,"bounded delayed one-shot execution without task retries");
        Assert(task.ActionCount==1 && task.TriggerCount==1 && task.Arguments=="--startup-connect" && task.WorkingDirectory==backend.Root,"single dedicated action and trigger");
        Assert(task.Security==StartupConnection.TaskSecurity(backend.Sid),"protected task security only allows administrator writes");
        var definition=new FakeStartupDefinition();
        StartupConnection.ConfigureTask(definition,backend.State,backend.Sid,backend.Root);
        var configured=StartupConnection.DescribeTask(definition,true,StartupConnection.TaskSecurity(backend.Sid));
        StartupConnection.ValidateTask(configured,backend.State,backend.Sid,backend.Root);
        Assert(definition.Triggers.Count==1 && definition.Actions.Count==1,"task configuration and projection agree through synthetic COM-compatible objects");
        var healthy=Enabled();
        healthy.Current.GccPid=12; healthy.Current.BootUtc="2000-01-01T00:00:00Z"; healthy.Current.GccStartUtc="2000-01-01T00:01:00Z";
        healthy.Current.Evidence=new LogEvidence { CurrentSession=true,Connected=true,Modules=2,SessionUtc="2000-01-01T00:01:01Z" };
        Assert(StartupConnection.Run(healthy)==0 && healthy.Connects==0 && healthy.Result.Success,"healthy native connection exits without recovery");
        var disconnected=Enabled();
        Assert(StartupConnection.Run(disconnected)==0 && disconnected.Connects==1 && disconnected.Result.Success,"disconnected session performs exactly one existing connection");
        var drift=Enabled(); drift.Current.Fingerprint=new string('2',64);
        Assert(StartupConnection.Run(drift)!=0 && drift.Connects==0 && !drift.Result.Success,"component drift fails without refreshing baseline or connecting");
        Assert(drift.State.Fingerprint==new string('1',64),"failed run preserves enrolled component baseline");
        var blocked=Enabled(); blocked.Current.IcueRunning=true;
        Assert(StartupConnection.Run(blocked)!=0 && blocked.Connects==0,"unsupported current environment never connects");
        var failed=Enabled(); failed.FailConnect=true;
        Assert(StartupConnection.Run(failed)!=0 && failed.Connects==1 && !failed.Result.Success,"connection failure recorded and never retried");
        var changedFile=Enabled(); changedFile.CorruptHash=true;
        Assert(StartupConnection.Run(changedFile)!=0 && changedFile.Connects==0,"installed executable hash drift rejected");
        var changedStatus=StartupConnection.ReadStatus(changedFile);
        Assert(!changedStatus.Enabled && changedStatus.CanDisable,"owned executable hash drift remains explicitly cleanable");
        var missingProgram=Enabled(); missingProgram.MissingInstalled=true;
        var missingStatus=StartupConnection.ReadStatus(missingProgram);
        Assert(!missingStatus.Enabled && missingStatus.CanDisable && missingStatus.Message.Contains("synthetic missing"),"missing installed executable remains visibly cleanable after ownership is confirmed");
        StartupConnection.Disable(missingProgram);
        Assert(missingProgram.Task==null&&!missingProgram.State.Enabled,"missing executable cannot block revocation");
        var brokenResult=Enabled(); brokenResult.FailResultRead=true;
        var brokenResultStatus=StartupConnection.ReadStatus(brokenResult);
        Assert(!brokenResultStatus.Enabled && brokenResultStatus.CanDisable && brokenResultStatus.Message.Contains("结果读取失败"),"damaged or unreadable result remains visibly cleanable after ownership is confirmed");
        StartupConnection.Disable(brokenResult);
        Assert(brokenResult.Task==null&&!brokenResult.State.Enabled,"result read failure cannot block revocation");
        var changedTask=Enabled(); changedTask.Task.Arguments="--watch";
        var changedTaskStatus=StartupConnection.ReadStatus(changedTask);
        Assert(!changedTaskStatus.Enabled && changedTaskStatus.CanDisable,"owned changed task remains explicitly cleanable");
        StartupConnection.Disable(changedTask);
        Assert(!StartupConnection.ReadStatus(changedTask).CanDisable,"clean disabled enrollment allows re-enabling");
        StartupConnection.Enable(changedTask.Current.Fingerprint,changedTask);
        Assert(StartupConnection.ReadStatus(changedTask).Enabled,"disable and re-enable registers a fresh explicit authorization");
        var wrongProgram=Enabled(); wrongProgram.Executable=@"C:\Downloads\CorsairTakeover.exe";
        Assert(StartupConnection.Run(wrongProgram)!=0 && wrongProgram.Connects==0,"runner rejects execution outside the installed authorized program");
        Reject(()=>StartupConnection.ValidateTaskSecurity("O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;"+backend.Sid+")",backend.Sid),"task DACL rejects ordinary-user write access");
        Reject(()=>StartupConnection.ValidateTaskSecurity("O:"+backend.Sid+"G:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;"+backend.Sid+")",backend.Sid),"task owner cannot be an unelevated user");
        var disabled=Enabled(); disabled.State.Enabled=false;
        Assert(StartupConnection.Run(disabled)!=0 && disabled.Connects==0,"disabled enrollment cannot connect");
        var wrongOwner=Enabled(); wrongOwner.State.OwnerSid="S-1-5-18";
        Assert(StartupConnection.Run(wrongOwner)!=0 && wrongOwner.Connects==0,"foreign authorization owner rejected");
        var readFailure=Enabled(); readFailure.FailRead=true;
        Reject(()=>StartupConnection.ReadStatus(readFailure),"read failures remain unknown instead of disabled");
        Assert(StartupConnection.Run(readFailure)!=0 && readFailure.Connects==0,"read failure exits with recorded failure");
        var resultFailure=Enabled(); resultFailure.FailResult=true;
        Assert(StartupConnection.Run(resultFailure)!=0,"failed result persistence cannot report successful run");
        var foreign=new FakeStartupBackend { Task=new StartupTask { Source="foreign",OwnerSid="S-1-5-18" } };
        Reject(()=>StartupConnection.Enable(foreign.Current.Fingerprint,foreign),"same-name foreign task blocks enable");
        Reject(()=>StartupConnection.Disable(foreign),"same-name foreign task blocks disable");
        Assert(foreign.Removes==0 && foreign.Saves==0,"foreign task and state remain untouched");
        var badRegistration=new FakeStartupBackend { FailRegister=true };
        Reject(()=>StartupConnection.Enable(badRegistration.Current.Fingerprint,badRegistration),"registration failure remains failure");
        Assert(badRegistration.State!=null&&!badRegistration.State.Enabled,"failed registration retains disabled recoverable authorization");
        var badReadback=new FakeStartupBackend { CorruptTask=true };
        Reject(()=>StartupConnection.Enable(badReadback.Current.Fingerprint,badReadback),"unexpected registered task action fails readback");
        Assert(!badReadback.State.Enabled && badReadback.Task==null,"failed task readback rolls back owned task and authorization");
        var saveFailure=new FakeStartupBackend { FailEnabledSave=true };
        Reject(()=>StartupConnection.Enable(saveFailure.Current.Fingerprint,saveFailure),"final authorization save failure remains failure");
        Assert(!saveFailure.State.Enabled && saveFailure.Task==null,"partial enable is rolled back");
        var partial=Enabled(); partial.FailRemove=true;
        Reject(()=>StartupConnection.Disable(partial),"task removal failure is explicit partial failure");
        Assert(!partial.State.Enabled && partial.Task!=null,"partial disable revokes authorization before reporting task failure");
        Assert(StartupConnection.ReadStatus(partial).CanDisable,"partial disable remains recoverable");
        partial.FailRemove=false; StartupConnection.Disable(partial); StartupConnection.Disable(partial);
        Assert(!partial.State.Enabled && partial.Task==null,"repeated disable safely removes only owned task");
        var orphan=Enabled(); orphan.State=null;
        Assert(StartupConnection.ReadStatus(orphan).CanDisable,"owned orphan task can be removed");
        StartupConnection.Disable(orphan);
        Assert(orphan.Task==null,"orphan task removed without original enrollment");
        Console.WriteLine("Startup synthetic checks passed: "+checks);
    }
}

public sealed class FakeMissingStartupFolder {
    public Exception Failure;
    public object GetTask(string name) { throw Failure; }
}

public sealed class FakeStartupDefinition {
    public FakeStartupRegistration RegistrationInfo { get; set; }
    public FakeStartupPrincipal Principal { get; set; }
    public FakeStartupSettings Settings { get; set; }
    public FakeStartupTriggers Triggers { get; set; }
    public FakeStartupActions Actions { get; set; }
    public FakeStartupDefinition() { RegistrationInfo=new FakeStartupRegistration(); Principal=new FakeStartupPrincipal(); Settings=new FakeStartupSettings(); Triggers=new FakeStartupTriggers(); Actions=new FakeStartupActions(); }
}
public sealed class FakeStartupRegistration { public string Source { get; set; } public string Description { get; set; } }
public sealed class FakeStartupPrincipal { public string UserId { get; set; } public int LogonType { get; set; } public int RunLevel { get; set; } }
public sealed class FakeStartupSettings {
    public bool Enabled { get; set; } public bool AllowDemandStart { get; set; } public bool StartWhenAvailable { get; set; }
    public bool DisallowStartIfOnBatteries { get; set; } public bool StopIfGoingOnBatteries { get; set; } public bool WakeToRun { get; set; }
    public bool Hidden { get; set; } public int MultipleInstances { get; set; } public int RestartCount { get; set; } public string ExecutionTimeLimit { get; set; }
}
public sealed class FakeStartupTrigger { public int Type { get; set; } public string UserId { get; set; } public string Delay { get; set; } public bool Enabled { get; set; } public FakeStartupRepetition Repetition { get; set; } public FakeStartupTrigger() { Repetition=new FakeStartupRepetition(); } }
public sealed class FakeStartupRepetition { public string Interval { get; set; } }
public sealed class FakeStartupTriggers {
    FakeStartupTrigger value;
    public int Count { get { return value==null?0:1; } }
    public FakeStartupTrigger Create(int type) { return value=new FakeStartupTrigger { Type=type }; }
    public FakeStartupTrigger this[int index] { get { if(index!=1) throw new IndexOutOfRangeException(); return value; } }
}
public sealed class FakeStartupAction { public int Type { get; set; } public string Path { get; set; } public string Arguments { get; set; } public string WorkingDirectory { get; set; } }
public sealed class FakeStartupActions {
    FakeStartupAction value;
    public int Count { get { return value==null?0:1; } }
    public FakeStartupAction Create(int type) { return value=new FakeStartupAction { Type=type }; }
    public FakeStartupAction this[int index] { get { if(index!=1) throw new IndexOutOfRangeException(); return value; } }
}
