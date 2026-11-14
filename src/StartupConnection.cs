using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CorsairTakeover {
    public sealed class StartupStatus {
        public bool Enabled;
        public bool CanDisable;
        public string Message;
        public OperationResult LastResult;
    }
    internal sealed class StartupEnrollment {
        public int Schema;
        public bool Enabled;
        public string OwnerSid;
        public string Fingerprint;
        public string InstalledExe;
        public string InstalledHash;
    }
    internal sealed class StartupTask {
        public bool Enabled, TriggerEnabled, AllowDemandStart, StartWhenAvailable, DisallowStartIfOnBatteries, StopIfGoingOnBatteries, WakeToRun, Hidden;
        public string Source, OwnerSid, TriggerSid, Delay, ExecutionTimeLimit, Path, Arguments, WorkingDirectory, RepetitionInterval, Security;
        public int LogonType, RunLevel, MultipleInstances, RestartCount, TriggerType, ActionType, TriggerCount, ActionCount;
    }
    internal interface IStartupBackend {
        string Sid { get; }
        string Root { get; }
        string CurrentExe { get; }
        void Prepare();
        StartupEnrollment Load();
        void Save(StartupEnrollment state);
        string Hash(string path);
        void VerifyFile(string path);
        void Install(string source,string destination,string hash);
        Snapshot Scan();
        string Connect(Snapshot snapshot);
        StartupTask ReadTask();
        void RegisterTask(StartupEnrollment state);
        void RemoveTask();
        OperationResult ReadResult();
        void SaveResult(OperationResult result);
    }
    public static class StartupConnection {
        internal const string Marker="CorsairTakeoverAssistant.Startup.v1";
        public static string Enable(string confirmedFingerprint) { return Enable(confirmedFingerprint,new WindowsStartupBackend()); }
        public static string Disable() { return Disable(new WindowsStartupBackend()); }
        public static StartupStatus ReadStatus() { return ReadStatus(new WindowsStartupBackend()); }
        public static int Run() { return Run(new WindowsStartupBackend()); }
        static bool IsHash(string value) { return value!=null && Regex.IsMatch(value,"^[A-Fa-f0-9]{64}$"); }
        internal static string InstalledPath(string root,string hash) { return System.IO.Path.Combine(root,"CorsairTakeover-startup-"+hash.ToUpperInvariant()+".exe"); }
        static void ValidateState(StartupEnrollment state,IStartupBackend backend) {
            if(state.Schema!=1 || state.OwnerSid!=backend.Sid || !IsHash(state.Fingerprint) || !IsHash(state.InstalledHash) ||
                !String.Equals(state.InstalledExe,InstalledPath(backend.Root,state.InstalledHash),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("登录自动连接授权格式、归属或安装路径不正确，已停止操作。");
        }
        static void VerifyInstalled(StartupEnrollment state,IStartupBackend backend) {
            backend.VerifyFile(state.InstalledExe);
            if(!String.Equals(backend.Hash(state.InstalledExe),state.InstalledHash,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("登录自动连接程序校验失败，请先关闭后重新开启。");
        }
        internal static void RequireOwnedTask(StartupTask task,string sid) {
            if(task.Source!=Marker || task.OwnerSid!=sid)
                throw new InvalidOperationException("同名登录任务不属于本工具当前用户，拒绝修改。");
        }
        internal static string TaskSecurity(string sid) {
            return "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;"+sid+")";
        }
        internal static void ValidateTaskSecurity(string security,string sid) {
            var descriptor=new RawSecurityDescriptor(security);
            if(descriptor.Owner==null || (descriptor.Owner.Value!="S-1-5-32-544" && descriptor.Owner.Value!="S-1-5-18"))
                throw new InvalidOperationException("登录任务所有者未受到管理员保护。");
            if((descriptor.ControlFlags&ControlFlags.DiscretionaryAclProtected)==0 || descriptor.DiscretionaryAcl==null)
                throw new InvalidOperationException("登录任务权限未受到保护。");
            bool admins=false,system=false,user=false;
            foreach(GenericAce entry in descriptor.DiscretionaryAcl) {
                var ace=entry as CommonAce;
                if(ace==null || ace.AceQualifier!=AceQualifier.AccessAllowed || ace.IsCallback)
                    throw new InvalidOperationException("登录任务包含不支持的权限规则。");
                string identity=ace.SecurityIdentifier.Value;
                if(identity=="S-1-5-32-544") admins=true;
                else if(identity=="S-1-5-18") system=true;
                else if(identity==sid) {
                    user=true;
                    const int allowed=0x1200a9; // FILE_GENERIC_READ | FILE_GENERIC_EXECUTE.
                    if((ace.AccessMask&~allowed)!=0) throw new InvalidOperationException("登录任务允许普通用户修改，已停止操作。");
                } else throw new InvalidOperationException("登录任务包含其他账户权限，已停止操作。");
            }
            if(!admins || !system || !user) throw new InvalidOperationException("登录任务访问权限不完整。");
        }
        internal static StartupTask ExpectedTask(StartupEnrollment state,string sid,string root) {
            return new StartupTask { Enabled=true,TriggerEnabled=true,Source=Marker,OwnerSid=sid,LogonType=3,RunLevel=1,
                TriggerCount=1,TriggerType=9,TriggerSid=sid,Delay="PT25S",ActionCount=1,ActionType=0,
                Path=state.InstalledExe,Arguments="--startup-connect",WorkingDirectory=root,MultipleInstances=2,
                ExecutionTimeLimit="PT5M",RestartCount=0,AllowDemandStart=true,Hidden=true,Security=TaskSecurity(sid) };
        }
        internal static void ValidateTask(StartupTask task,StartupEnrollment state,string sid,string root) {
            RequireOwnedTask(task,sid);
            ValidateTaskSecurity(task.Security,sid);
            if(!task.Enabled || !task.TriggerEnabled || task.LogonType!=3 || task.RunLevel!=1 || task.TriggerCount!=1 || task.TriggerType!=9 ||
                task.TriggerSid!=sid || task.Delay!="PT25S" || task.ActionCount!=1 || task.ActionType!=0 || task.Arguments!="--startup-connect" ||
                !String.Equals(task.Path,state.InstalledExe,StringComparison.OrdinalIgnoreCase) || !String.Equals(task.WorkingDirectory,root,StringComparison.OrdinalIgnoreCase) ||
                task.MultipleInstances!=2 || task.ExecutionTimeLimit!="PT5M" || task.RestartCount!=0 || !String.IsNullOrEmpty(task.RepetitionInterval) ||
                !task.AllowDemandStart || task.StartWhenAvailable || task.DisallowStartIfOnBatteries || task.StopIfGoingOnBatteries || task.WakeToRun || !task.Hidden)
                throw new InvalidOperationException("登录自动连接任务与已登记配置不一致，请先关闭后重新开启。");
        }
        internal static string Enable(string confirmedFingerprint,IStartupBackend backend) {
            if(!IsHash(confirmedFingerprint)) throw new InvalidOperationException("请先重新检测并确认组件基线。");
            backend.Prepare();
            var previous=backend.Load();
            if(previous!=null) {
                ValidateState(previous,backend);
                if(previous.Enabled) throw new InvalidOperationException("登录自动连接已启用。更换程序或组件基线前请先关闭，再重新开启。");
            }
            var existing=backend.ReadTask();
            if(existing!=null) {
                RequireOwnedTask(existing,backend.Sid);
                throw new InvalidOperationException("已有登录任务，请先关闭以清理旧配置，再重新开启。");
            }
            Snapshot snapshot=backend.Scan();
            if(snapshot.Fingerprint!=confirmedFingerprint) throw new InvalidOperationException("组件在确认后发生变化，请重新检测后再开启。");
            string blocked=GigabyteRecovery.ConnectionBlockReason(snapshot);
            if(blocked!=null) throw new InvalidOperationException(blocked);
            string hash=backend.Hash(backend.CurrentExe);
            if(!IsHash(hash)) throw new InvalidOperationException("当前程序哈希读取失败。");
            var state=new StartupEnrollment { Schema=1,Enabled=false,OwnerSid=backend.Sid,Fingerprint=snapshot.Fingerprint,
                InstalledHash=hash.ToUpperInvariant(),InstalledExe=InstalledPath(backend.Root,hash) };
            backend.Install(backend.CurrentExe,state.InstalledExe,state.InstalledHash);
            VerifyInstalled(state,backend);
            backend.Save(state);
            try {
                backend.RegisterTask(state);
                var task=backend.ReadTask();
                if(task==null) throw new InvalidOperationException("登录任务注册后无法读回。");
                ValidateTask(task,state,backend.Sid,backend.Root);
                state.Enabled=true;
                backend.Save(state);
                var saved=backend.Load();
                if(saved==null || !saved.Enabled) throw new InvalidOperationException("登录自动连接授权保存后无法确认。");
                ValidateState(saved,backend);
                if(saved.Fingerprint!=state.Fingerprint || saved.InstalledHash!=state.InstalledHash || saved.InstalledExe!=state.InstalledExe)
                    throw new InvalidOperationException("登录自动连接授权读回与本次登记不一致。");
                return "已开启登录自动连接。下次登录后延迟 25 秒检查，确认连接后静默退出。";
            } catch(Exception failure) {
                var errors=new List<Exception>{failure};
                state.Enabled=false;
                try { backend.Save(state); } catch(Exception ex) { errors.Add(ex); }
                try {
                    var task=backend.ReadTask();
                    if(task!=null) { RequireOwnedTask(task,backend.Sid); backend.RemoveTask(); }
                } catch(Exception ex) { errors.Add(ex); }
                if(errors.Count>1) throw new AggregateException("启用失败，清理也未全部完成，请在设置中关闭并查看状态。",errors);
                throw;
            }
        }
        internal static string Disable(IStartupBackend backend) {
            backend.Prepare();
            var state=backend.Load();
            if(state!=null) ValidateState(state,backend);
            var task=backend.ReadTask();
            if(task!=null) RequireOwnedTask(task,backend.Sid);
            if(state!=null) { state.Enabled=false; backend.Save(state); }
            if(task!=null) {
                try { backend.RemoveTask(); }
                catch(Exception ex) { throw new InvalidOperationException("登录自动连接授权已关闭，删除登录任务未完成："+ex.Message,ex); }
            }
            var saved=backend.Load();
            if(saved!=null && saved.Enabled) throw new InvalidOperationException("关闭授权后读回仍为启用，撤销未完成。");
            if(backend.ReadTask()!=null) throw new InvalidOperationException("授权已关闭，但登录任务仍然存在，请再次关闭。");
            return "已关闭登录自动连接。";
        }
        internal static StartupStatus ReadStatus(IStartupBackend backend) {
            var state=backend.Load();
            if(state!=null) ValidateState(state,backend);
            var task=backend.ReadTask();
            if(task!=null) RequireOwnedTask(task,backend.Sid);
            var status=new StartupStatus { CanDisable=(state!=null&&state.Enabled)||task!=null };
            string resultError=null;
            try { status.LastResult=backend.ReadResult(); }
            catch(Exception ex) {
                if(!StatusReadFailure(ex)) throw;
                resultError="上次登录连接结果读取失败："+ex.Message;
            }
            if(state==null || !state.Enabled || task==null) {
                status.Message=task!=null?"授权未启用，但仍有本工具登录任务，请关闭以清理。":
                    state!=null&&state.Enabled?"授权已保存，但登录任务缺失，请关闭后重新开启。":"登录自动连接未开启。";
                if(resultError!=null) status.Message+="\n"+resultError;
                return status;
            }
            try {
                VerifyInstalled(state,backend);
                ValidateTask(task,state,backend.Sid,backend.Root);
            } catch(Exception ex) {
                if(!StatusReadFailure(ex)) throw;
                status.Message="登录自动连接完整性检查失败："+ex.Message;
                if(resultError!=null) status.Message+="\n"+resultError;
                return status;
            }
            if(resultError!=null) { status.Message=resultError; return status; }
            status.Enabled=true;
            status.Message="登录自动连接已开启，连接确认后静默退出。";
            return status;
        }
        static bool StatusReadFailure(Exception failure) {
            return failure is IOException || failure is UnauthorizedAccessException || failure is InvalidOperationException || failure is ArgumentException;
        }
        internal static string Execute(IStartupBackend backend) {
            var state=backend.Load();
            if(state==null) throw new InvalidOperationException("没有当前用户的登录自动连接授权。");
            ValidateState(state,backend);
            if(!state.Enabled) throw new InvalidOperationException("登录自动连接授权已关闭。");
            if(!String.Equals(backend.CurrentExe,state.InstalledExe,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("登录自动连接必须从已登记的受保护程序运行。");
            VerifyInstalled(state,backend);
            var task=backend.ReadTask();
            if(task==null) throw new InvalidOperationException("登录自动连接任务已不存在。");
            ValidateTask(task,state,backend.Sid,backend.Root);
            Snapshot snapshot=backend.Scan();
            if(snapshot.Fingerprint!=state.Fingerprint) throw new InvalidOperationException("组件基线已变化，自动连接已停止，请关闭后重新检测并开启。");
            string blocked=GigabyteRecovery.ConnectionBlockReason(snapshot);
            if(blocked!=null) throw new InvalidOperationException(blocked);
            if(GigabyteRecovery.IsConnected(snapshot)) return "当前连接已确认，未执行恢复。";
            return backend.Connect(snapshot);
        }
        internal static int Run(IStartupBackend backend) {
            OperationResult result;
            try {
                backend.Prepare();
                using(var mutex=new Mutex(false,"Local\\CorsairTakeoverOperation-"+backend.Sid)) {
                    bool held=false;
                    try { held=mutex.WaitOne(2000); } catch(AbandonedMutexException) { held=true; }
                    if(!held) throw new InvalidOperationException("另一个连接或设置操作正在执行，本次登录连接已结束。");
                    try { result=new OperationResult { Success=true,Message=Execute(backend),AtUtc=DateTime.UtcNow.ToString("o") }; }
                    finally { mutex.ReleaseMutex(); }
                }
            } catch(Exception ex) { result=new OperationResult { Success=false,Message=ex.Message,AtUtc=DateTime.UtcNow.ToString("o") }; }
            try { backend.SaveResult(result); }
            catch(Exception ex) { System.Diagnostics.Trace.WriteLine("保存登录连接结果失败："+ex); return 1; }
            return result.Success?0:1;
        }
        internal static void ConfigureTask(dynamic definition,StartupEnrollment state,string sid,string root) {
            dynamic registration=definition.RegistrationInfo,principal=definition.Principal,settings=definition.Settings,triggers=definition.Triggers,actions=definition.Actions;
            dynamic trigger=null,action=null;
            try {
                registration.Source=Marker;
                registration.Description="海盗船内存接管助手：登录后单次连接，确认成功后退出。";
                principal.UserId=sid; principal.LogonType=3; principal.RunLevel=1;
                settings.Enabled=true; settings.AllowDemandStart=true; settings.StartWhenAvailable=false;
                settings.DisallowStartIfOnBatteries=false; settings.StopIfGoingOnBatteries=false; settings.WakeToRun=false; settings.Hidden=true;
                settings.MultipleInstances=2; settings.ExecutionTimeLimit="PT5M"; settings.RestartCount=0;
                trigger=triggers.Create(9); trigger.UserId=sid; trigger.Delay="PT25S"; trigger.Enabled=true;
                action=actions.Create(0); action.Path=state.InstalledExe; action.Arguments="--startup-connect"; action.WorkingDirectory=root;
            } finally { Release(action); Release(trigger); Release(actions); Release(triggers); Release(settings); Release(principal); Release(registration); }
        }
        static string TaskSid(string identity) {
            if(String.IsNullOrEmpty(identity)) throw new InvalidOperationException("登录任务缺少用户身份。");
            if(identity.StartsWith("S-",StringComparison.OrdinalIgnoreCase)) return new SecurityIdentifier(identity).Value;
            return ((SecurityIdentifier)new NTAccount(identity).Translate(typeof(SecurityIdentifier))).Value;
        }
        internal static StartupTask DescribeTask(dynamic definition,bool enabled,string security) {
            dynamic registration=definition.RegistrationInfo,principal=definition.Principal,settings=definition.Settings,triggers=definition.Triggers,actions=definition.Actions;
            dynamic trigger=null,action=null,repetition=null;
            try {
                var task=new StartupTask { Enabled=enabled&&(bool)settings.Enabled,Source=(string)registration.Source,OwnerSid=TaskSid((string)principal.UserId),
                    LogonType=(int)principal.LogonType,RunLevel=(int)principal.RunLevel,TriggerCount=(int)triggers.Count,ActionCount=(int)actions.Count,
                    AllowDemandStart=(bool)settings.AllowDemandStart,StartWhenAvailable=(bool)settings.StartWhenAvailable,
                    DisallowStartIfOnBatteries=(bool)settings.DisallowStartIfOnBatteries,StopIfGoingOnBatteries=(bool)settings.StopIfGoingOnBatteries,
                    WakeToRun=(bool)settings.WakeToRun,Hidden=(bool)settings.Hidden,MultipleInstances=(int)settings.MultipleInstances,
                    ExecutionTimeLimit=(string)settings.ExecutionTimeLimit,RestartCount=(int)settings.RestartCount,Security=security };
                if(task.TriggerCount==1) {
                    trigger=triggers[1]; task.TriggerType=(int)trigger.Type; task.TriggerEnabled=(bool)trigger.Enabled;
                    if(task.TriggerType==9) { task.TriggerSid=TaskSid((string)trigger.UserId); task.Delay=(string)trigger.Delay; }
                    repetition=trigger.Repetition; task.RepetitionInterval=(string)repetition.Interval;
                }
                if(task.ActionCount==1) {
                    action=actions[1]; task.ActionType=(int)action.Type;
                    if(task.ActionType==0) { task.Path=(string)action.Path; task.Arguments=(string)action.Arguments; task.WorkingDirectory=(string)action.WorkingDirectory; }
                }
                return task;
            } finally { Release(repetition); Release(action); Release(trigger); Release(actions); Release(triggers); Release(settings); Release(principal); Release(registration); }
        }
        internal static void Release(object value) { if(value!=null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    }
    internal sealed class WindowsStartupBackend : IStartupBackend {
        public string Sid { get { return StateStore.Sid; } }
        public string Root { get { return StateStore.Root; } }
        public string CurrentExe { get { return Assembly.GetExecutingAssembly().Location; } }
        string StatePath { get { return System.IO.Path.Combine(Root,"startup-"+Sid+".json"); } }
        string ResultPath { get { return System.IO.Path.Combine(Root,"startup-result-"+Sid+".json"); } }
        string TaskName { get { return TaskControl.Name+"-Startup"; } }
        public void Prepare() { StateStore.Prepare(); }
        public string Hash(string path) { return Probe.Hash(path); }
        public Snapshot Scan() { return Probe.Scan(); }
        public string Connect(Snapshot snapshot) { return GigabyteRecovery.Connect(snapshot); }
        static void CheckAcl(FileSystemSecurity security) {
            string owner=((SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier))).Value;
            if(owner!="S-1-5-32-544" && owner!="S-1-5-18") throw new IOException("登录自动连接存储归属不安全，停止操作。");
            foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier))) {
                string identity=((SecurityIdentifier)rule.IdentityReference).Value;
                if(rule.AccessControlType==AccessControlType.Allow && identity!="S-1-5-32-544" && identity!="S-1-5-18" &&
                    (rule.FileSystemRights&(FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles|FileSystemRights.ChangePermissions|FileSystemRights.TakeOwnership))!=0)
                    throw new IOException("登录自动连接存储存在非管理员写权限，停止操作。");
            }
        }
        bool RootExists() {
            FileAttributes attributes;
            try { attributes=File.GetAttributes(Root); }
            catch(FileNotFoundException) { return false; }
            catch(DirectoryNotFoundException) { return false; }
            if((attributes&FileAttributes.Directory)==0) throw new IOException("登录自动连接存储目录位置异常。");
            if((attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("登录自动连接存储目录是重解析点。");
            CheckAcl(Directory.GetAccessControl(Root));
            return true;
        }
        public void VerifyFile(string path) {
            if(!RootExists() || !String.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)),Root,StringComparison.OrdinalIgnoreCase))
                throw new IOException("登录自动连接文件不在受保护目录。");
            var attributes=File.GetAttributes(path);
            if((attributes&(FileAttributes.ReparsePoint|FileAttributes.Directory))!=0) throw new IOException("登录自动连接文件位置异常。");
            CheckAcl(File.GetAccessControl(path));
        }
        string Read(string path) {
            if(!RootExists()) return null;
            try { File.GetAttributes(path); } catch(FileNotFoundException) { return null; }
            VerifyFile(path);
            return File.ReadAllText(path,Encoding.UTF8);
        }
        static T Required<T>(Dictionary<string,object> record,string name) {
            object value;
            if(record==null || !record.TryGetValue(name,out value) || !(value is T))
                throw new InvalidOperationException("登录自动连接记录缺少有效字段："+name);
            return (T)value;
        }
        internal static StartupEnrollment ParseState(string json) {
            var record=Json.Read<Dictionary<string,object>>(json);
            return new StartupEnrollment { Schema=Required<int>(record,"Schema"),Enabled=Required<bool>(record,"Enabled"),
                OwnerSid=Required<string>(record,"OwnerSid"),Fingerprint=Required<string>(record,"Fingerprint"),
                InstalledExe=Required<string>(record,"InstalledExe"),InstalledHash=Required<string>(record,"InstalledHash") };
        }
        internal static OperationResult ParseResult(string json) {
            var record=Json.Read<Dictionary<string,object>>(json);
            var result=new OperationResult { Success=Required<bool>(record,"Success"),Message=Required<string>(record,"Message"),AtUtc=Required<string>(record,"AtUtc") };
            DateTime at;
            if(String.IsNullOrEmpty(result.Message) || !DateTime.TryParse(result.AtUtc,null,System.Globalization.DateTimeStyles.RoundtripKind,out at) || at.Kind!=DateTimeKind.Utc)
                throw new InvalidOperationException("登录自动连接结果记录格式不正确。");
            return result;
        }
        public StartupEnrollment Load() { string json=Read(StatePath); return json==null?null:ParseState(json); }
        public OperationResult ReadResult() { string json=Read(ResultPath); return json==null?null:ParseResult(json); }
        FileSecurity FileAcl() {
            var security=new FileSecurity();
            security.SetAccessRuleProtection(true,false);
            security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null),FileSystemRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),FileSystemRights.FullControl,AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Sid),FileSystemRights.ReadAndExecute,AccessControlType.Allow));
            return security;
        }
        FileStream CreateSecured(string path) {
            return new FileStream(path,FileMode.CreateNew,FileSystemRights.Write,FileShare.None,4096,FileOptions.None,FileAcl());
        }
        void Write(string path,object value) {
            Prepare();
            if(File.Exists(path)) VerifyFile(path);
            string temporary=System.IO.Path.Combine(Root,"startup-write-"+Guid.NewGuid().ToString("N")+".tmp");
            try {
                using(var stream=CreateSecured(temporary)) using(var writer=new StreamWriter(stream,Encoding.UTF8)) { writer.Write(Json.Write(value)); writer.Flush(); stream.Flush(true); }
                if(File.Exists(path)) File.Replace(temporary,path,null); else File.Move(temporary,path);
                VerifyFile(path);
            } finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Save(StartupEnrollment state) { Write(StatePath,state); }
        public void SaveResult(OperationResult result) { Write(ResultPath,result); }
        public void Install(string source,string destination,string hash) {
            Prepare();
            if(!String.Equals(System.IO.Path.GetDirectoryName(destination),Root,StringComparison.OrdinalIgnoreCase)) throw new IOException("安装目标目录不正确。");
            if(!File.Exists(destination)) {
                string temporary=System.IO.Path.Combine(Root,"startup-install-"+Guid.NewGuid().ToString("N")+".tmp");
                try {
                    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
                    using(var output=CreateSecured(temporary)) { input.CopyTo(output); output.Flush(true); }
                    VerifyFile(temporary);
                    if(!String.Equals(Hash(temporary),hash,StringComparison.OrdinalIgnoreCase)) throw new IOException("安装程序复制后校验失败。");
                    File.Move(temporary,destination);
                } finally { if(File.Exists(temporary)) File.Delete(temporary); }
            }
            VerifyFile(destination);
            if(!String.Equals(Hash(destination),hash,StringComparison.OrdinalIgnoreCase)) throw new IOException("安装程序复制后校验失败。");
        }
        static object Scheduler() {
            dynamic scheduler=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            try { scheduler.Connect(); return scheduler; }
            catch { StartupConnection.Release(scheduler); throw; }
        }
        internal static object FindTask(dynamic root,string name) {
            try { return root.GetTask(name); }
            catch(Exception ex) { if((uint)ex.HResult==0x80070002) return null; throw; }
        }
        public StartupTask ReadTask() {
            dynamic scheduler=null,root=null,task=null,definition=null;
            try {
                scheduler=Scheduler(); root=scheduler.GetFolder("\\"); task=FindTask(root,TaskName);
                if(task==null) return null;
                definition=task.Definition;
                return StartupConnection.DescribeTask(definition,(bool)task.Enabled,(string)task.GetSecurityDescriptor(7));
            } finally { StartupConnection.Release(definition); StartupConnection.Release(task); StartupConnection.Release(root); StartupConnection.Release(scheduler); }
        }
        public void RegisterTask(StartupEnrollment state) {
            dynamic scheduler=null,root=null,definition=null,registered=null,existing=null;
            try {
                scheduler=Scheduler(); root=scheduler.GetFolder("\\"); existing=FindTask(root,TaskName);
                if(existing!=null) throw new InvalidOperationException("同名登录任务已经存在，未覆盖。");
                definition=scheduler.NewTask(0);
                StartupConnection.ConfigureTask(definition,state,Sid,Root);
                // TASK_CREATE | TASK_DONT_ADD_PRINCIPAL_ACE preserves the read/execute-only user ACE.
                registered=root.RegisterTaskDefinition(TaskName,definition,2|0x10,Sid,null,3,StartupConnection.TaskSecurity(Sid));
            } finally { StartupConnection.Release(existing); StartupConnection.Release(registered); StartupConnection.Release(definition); StartupConnection.Release(root); StartupConnection.Release(scheduler); }
        }
        public void RemoveTask() {
            dynamic scheduler=null,root=null,task=null,definition=null;
            try {
                scheduler=Scheduler(); root=scheduler.GetFolder("\\"); task=FindTask(root,TaskName);
                if(task==null) return;
                definition=task.Definition;
                var record=StartupConnection.DescribeTask(definition,(bool)task.Enabled,(string)task.GetSecurityDescriptor(7));
                StartupConnection.RequireOwnedTask(record,Sid);
                var errors=new List<Exception>();
                try { if((int)task.State==4) task.Stop(0); } catch(Exception ex) { errors.Add(ex); }
                try { root.DeleteTask(TaskName,0); } catch(Exception ex) { errors.Add(ex); }
                if(errors.Count>0) throw new AggregateException("清理登录任务未全部完成。",errors);
            } finally { StartupConnection.Release(definition); StartupConnection.Release(task); StartupConnection.Release(root); StartupConnection.Release(scheduler); }
        }
    }
}
