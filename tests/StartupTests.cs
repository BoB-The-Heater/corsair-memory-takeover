using System;
using System.IO;
using System.Collections.Generic;
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
    public Snapshot ConnectionBaseline;
    public readonly Queue<Snapshot> Observations=new Queue<Snapshot>();
    public readonly Queue<Snapshot> ScanResults=new Queue<Snapshot>();
    public TimeSpan Elapsed { get; private set; }
    public int Connects, Registers, Removes, Saves, Scans, Reads, Waits;
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
    public Snapshot Scan() { Scans++; if(FailRead) throw new IOException("synthetic scan failure"); return ScanResults.Count>0?ScanResults.Dequeue():Current; }
    public Snapshot Observe(Snapshot baseline) { Reads++; return Observations.Count>0?Observations.Dequeue():Current; }
    public void Wait(TimeSpan delay) { Waits++; Elapsed+=delay; }
    public string Connect(Snapshot snapshot) { Connects++; ConnectionBaseline=snapshot; if(FailConnect) throw new IOException("synthetic connection failure"); return "synthetic connection confirmed"; }
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
    static Snapshot Initializing(bool connected=false) {
        var snapshot=Sample();
        snapshot.BootUtc="2000-01-01T12:08:06Z";
        snapshot.GccPid=8228;
        snapshot.GccStartUtc="2000-01-01T12:08:39Z";
        snapshot.Evidence=new LogEvidence { CurrentSession=true,Connected=connected,Modules=connected?2:0,SessionUtc="2000-01-01T12:08:39Z" };
        return snapshot;
    }
    static Snapshot Faulted(string serviceState) {
        var snapshot=Initializing();
        snapshot.DcsState=serviceState;
        snapshot.Evidence.InstalledWriteFailures=1;
        return snapshot;
    }
    static void RejectStoppedVerification(Snapshot verified,string message) {
        var backend=Enabled(); backend.Current=Initializing();
        backend.Observations.Enqueue(Faulted("Stopped"));
        backend.ScanResults.Enqueue(backend.Current); backend.ScanResults.Enqueue(verified);
        Assert(StartupConnection.Run(backend)!=0 && backend.Connects==0 && backend.Elapsed==TimeSpan.Zero && !backend.Result.Success,message);
    }
    static void InitializationTests() {
        // Synthetic times preserve the reported order: task at 12:08:41, native registration at 12:08:45.
        var timeline=Enabled(); timeline.Current=Initializing();
        for(int index=0;index<8;index++) timeline.Observations.Enqueue(Initializing());
        timeline.Observations.Enqueue(Initializing(true));
        timeline.ScanResults.Enqueue(timeline.Current); timeline.ScanResults.Enqueue(Initializing(true));
        Assert(StartupConnection.Run(timeline)==0 && timeline.Connects==0 && timeline.Result.Success,"native startup finishes four seconds after the initial scan without recovery");
        Assert(timeline.Elapsed==TimeSpan.FromSeconds(4) && timeline.Waits==8 && timeline.Scans==3,"startup observes lightly and performs a fresh full verification before natural success");
        var serviceFailure=Enabled(); serviceFailure.Current=Initializing();
        serviceFailure.Observations.Enqueue(Faulted("Running"));
        var stopping=Faulted("StopPending"); stopping.Evidence.PipeErrors=1;
        serviceFailure.Observations.Enqueue(stopping);
        var stopped=Faulted("Stopped"); stopped.Evidence.PipeErrors=1;
        serviceFailure.Observations.Enqueue(stopped);
        serviceFailure.ScanResults.Enqueue(serviceFailure.Current); serviceFailure.ScanResults.Enqueue(stopped);
        Assert(StartupConnection.Run(serviceFailure)==0 && serviceFailure.Connects==1 && serviceFailure.Result.Success,"write failure followed by pipe closure and native service stop reaches one recovery");
        Assert(serviceFailure.Elapsed==TimeSpan.FromSeconds(1) && serviceFailure.Reads==3 && Object.ReferenceEquals(serviceFailure.ConnectionBaseline,stopped),"initialization waits through running and stop-pending faults and recovers only after full stopped verification");
        var fault=Enabled(); fault.Current=Initializing(); fault.Current.Evidence.PipeErrors=1;
        Assert(StartupConnection.Run(fault)==0 && fault.Connects==1 && fault.Elapsed==TimeSpan.FromSeconds(38),"initial communication fault with a running service spends the original observation budget before one recovery");
        var writeFault=Enabled(); writeFault.Current=Initializing(); writeFault.Current.Evidence.InstalledWriteFailures=1;
        Assert(StartupConnection.Run(writeFault)==0 && writeFault.Connects==1 && writeFault.Elapsed==TimeSpan.FromSeconds(38),"initial installed-memory fault cannot interrupt a running service before the deadline");
        var initialStopped=Enabled(); initialStopped.Current=Faulted("Stopped"); var verifiedStopped=Faulted("Stopped");
        initialStopped.ScanResults.Enqueue(initialStopped.Current); initialStopped.ScanResults.Enqueue(verifiedStopped);
        Assert(StartupConnection.Run(initialStopped)==0 && initialStopped.Connects==1 && initialStopped.Waits==0 && initialStopped.Reads==1 && Object.ReferenceEquals(initialStopped.ConnectionBaseline,verifiedStopped),"initial fault with stopped service still requires a fresh matching full scan before one recovery");
        var naturalAfterFault=Enabled(); naturalAfterFault.Current=Faulted("Running");
        naturalAfterFault.Observations.Enqueue(Faulted("Running")); naturalAfterFault.Observations.Enqueue(Initializing(true));
        naturalAfterFault.ScanResults.Enqueue(naturalAfterFault.Current); naturalAfterFault.ScanResults.Enqueue(Initializing(true));
        Assert(StartupConnection.Run(naturalAfterFault)==0 && naturalAfterFault.Connects==0 && naturalAfterFault.Elapsed==TimeSpan.FromMilliseconds(500),"initial fault may clear through a naturally established connection without recovery");
        var resumed=Enabled(); resumed.Current=Faulted("Running");
        for(int index=0;index<4;index++) resumed.Observations.Enqueue(Faulted("Running"));
        resumed.Observations.Enqueue(Faulted("Stopped"));
        var runningAgain=Faulted("Running"); var finalRunning=Faulted("Running");
        resumed.ScanResults.Enqueue(resumed.Current); resumed.ScanResults.Enqueue(runningAgain); resumed.ScanResults.Enqueue(finalRunning);
        Assert(StartupConnection.Run(resumed)==0 && resumed.Connects==1 && resumed.Elapsed==TimeSpan.FromSeconds(38) && resumed.Waits==76,"service running again in the stopped-fault full scan continues the same observation deadline");
        Assert(resumed.Scans==4 && Object.ReferenceEquals(resumed.ConnectionBaseline,finalRunning),"resumed observation supplies the deadline full scan to the sole recovery");
        var stoppedWithoutFault=Enabled(); stoppedWithoutFault.Current=Initializing(); stoppedWithoutFault.Current.DcsState="Stopped";
        Assert(StartupConnection.Run(stoppedWithoutFault)==0 && stoppedWithoutFault.Connects==1 && stoppedWithoutFault.Elapsed==TimeSpan.FromSeconds(38),"stopped service alone cannot permit recovery before the deadline");
        var staleFault=Enabled(); staleFault.Current=Faulted("Stopped"); staleFault.Current.Evidence.SessionUtc="1999-01-01T00:00:00Z";
        Assert(StartupConnection.Run(staleFault)==0 && staleFault.Connects==1 && staleFault.Elapsed==TimeSpan.FromSeconds(38) && staleFault.Scans==3,"old-session fault cannot permit early stopped-service recovery");
        var unattributedFault=Enabled(); unattributedFault.Current=Faulted("Stopped"); unattributedFault.Current.Evidence.CurrentSession=false;
        Assert(StartupConnection.Run(unattributedFault)==0 && unattributedFault.Connects==1 && unattributedFault.Elapsed==TimeSpan.FromSeconds(38),"unattributed fault cannot permit early stopped-service recovery");
        var unverifiedFault=Enabled(); unverifiedFault.Current=Faulted("Running");
        unverifiedFault.Observations.Enqueue(Faulted("Stopped"));
        var oldStopped=Faulted("Stopped"); oldStopped.Evidence.SessionUtc="1999-01-01T00:00:00Z";
        unverifiedFault.ScanResults.Enqueue(unverifiedFault.Current); unverifiedFault.ScanResults.Enqueue(oldStopped); unverifiedFault.ScanResults.Enqueue(finalRunning);
        Assert(StartupConnection.Run(unverifiedFault)==0 && unverifiedFault.Connects==1 && unverifiedFault.Elapsed==TimeSpan.FromSeconds(38) && Object.ReferenceEquals(unverifiedFault.ConnectionBaseline,finalRunning),"full scan must independently confirm current-session fault before early recovery");
        var failedRecovery=Enabled(); failedRecovery.Current=Faulted("Stopped"); failedRecovery.FailConnect=true;
        Assert(StartupConnection.Run(failedRecovery)!=0 && failedRecovery.Connects==1 && !failedRecovery.Result.Success,"recovery failure after a verified native stop remains failed without retry");
        var changedStopped=Faulted("Stopped"); changedStopped.Fingerprint=new string('2',64);
        RejectStoppedVerification(changedStopped,"stopped-fault full scan rejects changed component baseline before recovery");
        var foreignStopped=Faulted("Stopped"); foreignStopped.GccPid++;
        RejectStoppedVerification(foreignStopped,"stopped-fault full scan rejects another GCC process before recovery");
        var reusedStopped=Faulted("Stopped"); reusedStopped.GccStartUtc="2000-01-01T12:08:42Z";
        RejectStoppedVerification(reusedStopped,"stopped-fault full scan rejects reused PID with another process start before recovery");
        var incompleteStopped=Faulted("Stopped"); incompleteStopped.Errors.Add("synthetic full scan read failure");
        RejectStoppedVerification(incompleteStopped,"stopped-fault full scan read failure prevents recovery");
        var conflictStopped=Faulted("Stopped"); conflictStopped.Evidence.Note="synthetic foreign controller";
        RejectStoppedVerification(conflictStopped,"stopped-fault full scan rejects ambiguous controller ownership");
        var occupiedStopped=Faulted("Stopped"); occupiedStopped.IcueRunning=true;
        RejectStoppedVerification(occupiedStopped,"stopped-fault full scan rejects iCUE occupation before recovery");
        var unsupportedStopped=Faulted("Stopped"); unsupportedStopped.DcsVersion="unsupported";
        RejectStoppedVerification(unsupportedStopped,"stopped-fault full scan repeats supported-component validation");
        RejectStoppedVerification(Faulted("StopPending"),"stopped-fault full scan keeps the existing strict pending-service rejection");
        var occupiedObservation=Enabled(); occupiedObservation.Current=Initializing();
        var occupied=Faulted("Stopped"); occupied.IcueRunning=true;
        occupiedObservation.Observations.Enqueue(occupied);
        Assert(StartupConnection.Run(occupiedObservation)!=0 && occupiedObservation.Connects==0 && occupiedObservation.Scans==2 && occupiedObservation.Waits==0,"stopped-fault observation rejects iCUE before requesting an early full scan");
        var expired=Enabled(); expired.Current=Initializing(); var fresh=Initializing();
        expired.ScanResults.Enqueue(expired.Current); expired.ScanResults.Enqueue(fresh);
        Assert(StartupConnection.Run(expired)==0 && expired.Elapsed==TimeSpan.FromSeconds(38) && expired.Connects==1,"unconfirmed stable initialization has one fixed deadline then one recovery");
        Assert(Object.ReferenceEquals(expired.ConnectionBaseline,fresh) && expired.Scans==3,"timeout recovery receives the fresh scan instead of the original startup baseline");
        var boundary=Enabled(); boundary.Current=Initializing();
        boundary.ScanResults.Enqueue(boundary.Current); boundary.ScanResults.Enqueue(Initializing(true));
        Assert(StartupConnection.Run(boundary)==0 && boundary.Elapsed==TimeSpan.FromSeconds(38) && boundary.Connects==0,"connection confirmed by the deadline full scan avoids recovery");
        var changed=Enabled(); changed.Current=Initializing(); var replacement=Initializing(); replacement.GccPid++;
        changed.Observations.Enqueue(replacement);
        Assert(StartupConnection.Run(changed)!=0 && changed.Connects==0 && changed.Waits==0 && !changed.Result.Success,"GCC process replacement during startup fails without recovery or budget reset");
        var reused=Enabled(); reused.Current=Initializing(); var newCreation=Initializing(); newCreation.GccStartUtc="2000-01-01T12:08:42Z";
        reused.Observations.Enqueue(newCreation);
        Assert(StartupConnection.Run(reused)!=0 && reused.Connects==0,"GCC PID reuse with changed creation time cannot inherit startup evidence");
        var vanished=Enabled(); vanished.Current=Initializing(); var absent=Initializing(); absent.GccPid=0; absent.GccStartUtc=null;
        vanished.Observations.Enqueue(absent);
        Assert(StartupConnection.Run(vanished)!=0 && vanished.Connects==0,"GCC disappearance during initialization cannot trigger recovery with old identity");
        var unreadable=Enabled(); unreadable.Current=Initializing(); var readError=Initializing(); readError.Errors.Add("synthetic initialization read failure");
        unreadable.Observations.Enqueue(readError);
        Assert(StartupConnection.Run(unreadable)!=0 && unreadable.Connects==0 && unreadable.Waits==0,"initialization read errors fail immediately without a destructive fallback");
        var ownership=Enabled(); ownership.Current=Initializing(); var conflict=Initializing(); conflict.Evidence.Note="synthetic another controller";
        ownership.Observations.Enqueue(conflict);
        Assert(StartupConnection.Run(ownership)!=0 && ownership.Connects==0,"uncertain controller ownership during startup cannot claim or recover the session");
        var initialConflict=Enabled(); initialConflict.Current=Initializing(); initialConflict.Current.Evidence.Note="synthetic another controller"; initialConflict.Current.Evidence.PipeErrors=1;
        Assert(StartupConnection.Run(initialConflict)!=0 && initialConflict.Connects==0 && initialConflict.Waits==0,"initial ownership conflict prevents recovery even when communication errors also exist");
        var newFault=Enabled(); newFault.Current=Initializing(); var faultObservation=Initializing(); faultObservation.Evidence.PipeErrors=1;
        newFault.Observations.Enqueue(faultObservation);
        Assert(StartupConnection.Run(newFault)==0 && newFault.Connects==1 && newFault.Elapsed==TimeSpan.FromSeconds(38),"new communication fault does not interrupt initialization and reaches only the existing deadline recovery");
        var faultySuccess=Enabled(); faultySuccess.Current=Initializing();
        var connectedWithFault=Initializing(true); connectedWithFault.Evidence.PipeErrors=1;
        faultySuccess.Observations.Enqueue(connectedWithFault);
        faultySuccess.ScanResults.Enqueue(faultySuccess.Current); faultySuccess.ScanResults.Enqueue(connectedWithFault);
        Assert(StartupConnection.Run(faultySuccess)==0 && faultySuccess.Connects==1 && faultySuccess.Elapsed==TimeSpan.FromSeconds(38),"connected log carrying a communication fault cannot claim natural success");
        Reject(()=>GigabyteRecovery.CheckObservation(connectedWithFault),"post-recovery new-session communication fault remains strictly rejected");
        Reject(()=>GigabyteRecovery.CheckObservation(Faulted("Running")),"post-recovery new-session installed-memory fault remains strictly rejected");
        var drift=Enabled(); drift.Current=Initializing(); drift.Observations.Enqueue(Initializing(true));
        var changedComponents=Initializing(true); changedComponents.Fingerprint=new string('2',64);
        drift.ScanResults.Enqueue(drift.Current); drift.ScanResults.Enqueue(changedComponents);
        Assert(StartupConnection.Run(drift)!=0 && drift.Connects==0,"natural success cannot bypass component fingerprint verification");
        var finalProcess=Enabled(); finalProcess.Current=Initializing(); finalProcess.Observations.Enqueue(Initializing(true));
        var replacedFinal=Initializing(true); replacedFinal.GccPid++;
        finalProcess.ScanResults.Enqueue(finalProcess.Current); finalProcess.ScanResults.Enqueue(replacedFinal);
        Assert(StartupConnection.Run(finalProcess)!=0 && finalProcess.Connects==0,"natural success requires the same process in the final full scan");
        var lostConnection=Enabled(); lostConnection.Current=Initializing(); lostConnection.Observations.Enqueue(Initializing(true));
        lostConnection.ScanResults.Enqueue(lostConnection.Current); lostConnection.ScanResults.Enqueue(Initializing());
        Assert(StartupConnection.Run(lostConnection)!=0 && lostConnection.Connects==0,"connection lost during natural-success verification is an explicit failure without recovery");
        var faultAfterSuccess=Enabled(); faultAfterSuccess.Current=Initializing(); faultAfterSuccess.Observations.Enqueue(Initializing(true));
        faultAfterSuccess.ScanResults.Enqueue(faultAfterSuccess.Current); faultAfterSuccess.ScanResults.Enqueue(connectedWithFault);
        Assert(StartupConnection.Run(faultAfterSuccess)!=0 && faultAfterSuccess.Connects==0 && !faultAfterSuccess.Result.Success,"new fault in natural-success full verification remains an explicit failure without recovery");
        var unsupported=Enabled(); unsupported.Current=Initializing(); unsupported.Observations.Enqueue(Initializing(true));
        var blockedFinal=Initializing(true); blockedFinal.IcueRunning=true;
        unsupported.ScanResults.Enqueue(unsupported.Current); unsupported.ScanResults.Enqueue(blockedFinal);
        Assert(StartupConnection.Run(unsupported)!=0 && unsupported.Connects==0,"natural success repeats the supported-environment guard");
        var stale=Enabled(); stale.Current=Initializing(); var oldLog=Initializing(true); oldLog.Evidence.SessionUtc="1999-01-01T00:00:00Z";
        stale.Observations.Enqueue(oldLog);
        Assert(StartupConnection.Run(stale)==0 && stale.Connects==1 && stale.Elapsed==TimeSpan.FromSeconds(38),"old log timestamps cannot end initialization observation as success");
        var deadlineChange=Enabled(); deadlineChange.Current=Initializing();
        deadlineChange.ScanResults.Enqueue(deadlineChange.Current); deadlineChange.ScanResults.Enqueue(replacement);
        Assert(StartupConnection.Run(deadlineChange)!=0 && deadlineChange.Connects==0 && deadlineChange.Elapsed==TimeSpan.FromSeconds(38),"deadline full scan rejects changed process before any recovery");
        var deadlineDrift=Enabled(); deadlineDrift.Current=Initializing();
        deadlineDrift.ScanResults.Enqueue(deadlineDrift.Current); deadlineDrift.ScanResults.Enqueue(changedComponents);
        Assert(StartupConnection.Run(deadlineDrift)!=0 && deadlineDrift.Connects==0,"deadline full scan rejects component drift before any recovery");
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
        InitializationTests();
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
