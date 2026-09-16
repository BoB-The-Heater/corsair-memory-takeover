using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        return new Snapshot { Brand="GIGABYTE",BoardModel="Synthetic board",Fingerprint="sample",GccPid=42,DcsState="Running",
            Ram=new List<RamInfo>{new RamInfo{Vendor="Corsair",Part="SYNTHETIC-RAM"},new RamInfo{Vendor="Corsair",Part="SYNTHETIC-RAM"}} };
    }
    static Snapshot Native(string brand) {
        return new Snapshot { Brand=brand,Fingerprint="fixture",IcueRunning=true,IcueVersion="fixture",Ram=new List<RamInfo>{new RamInfo{Vendor="Corsair",Part="SYNTHETIC-RAM"}},
            Requirements=new List<ComponentCheck>{new ComponentCheck{Name="fixture prerequisites",Present=true}},
            NativeServices=new List<NativeService>{new NativeService{Name="Corsair fixture service",Role="Corsair",Trusted=true,StartMode="Auto",State="Running"},new NativeService{Name=brand+" fixture light",Role="Lighting",Trusted=true,StartMode="Auto",State="Running"}} };
    }
    static Enrollment Enroll(Snapshot s) { return new Enrollment{Schema=2,Brand=s.Brand,Enabled=true,OwnerSid=StateStore.Sid,Fingerprint=s.Fingerprint,NativeConfirmedUtc="2000-01-01T01:00:00Z",BaselineServices=s.NativeServices.Select(v=>v.Name).ToList()}; }
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
