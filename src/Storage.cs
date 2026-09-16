using System;
using System.IO;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CorsairTakeover {
    public static class Json {
        public static string Write(object o) { return new JavaScriptSerializer().Serialize(o); }
        public static T Read<T>(string s) { return new JavaScriptSerializer().Deserialize<T>(s); }
    }
    public static class StateStore {
        public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"CorsairTakeoverAssistant");
        public static string StateFile { get { return Path.Combine(Root,"state.json"); } }
        public static string Sid { get { return WindowsIdentity.GetCurrent().User.Value; } }
        public static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
        public static void Prepare() {
            if(!Admin) throw new InvalidOperationException("此操作需要管理员权限。");
            if(Directory.Exists(Root)) {
                if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0) throw new IOException("设置目录为重解析点，停止操作。");
                var security=Directory.GetAccessControl(Root);
                var owner=(SecurityIdentifier)security.GetOwner(typeof(SecurityIdentifier));
                if(owner.Value!="S-1-5-32-544" && owner.Value!="S-1-5-18") throw new IOException("已有同名目录不属于本工具的管理员存储，停止操作。");
                foreach(FileSystemAccessRule rule in security.GetAccessRules(true,true,typeof(SecurityIdentifier))) {
                    string sid=((SecurityIdentifier)rule.IdentityReference).Value;
                    if(rule.AccessControlType==AccessControlType.Allow && sid!="S-1-5-32-544" && sid!="S-1-5-18" && (rule.FileSystemRights & (FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.ChangePermissions))!=0) throw new IOException("设置目录存在非管理员写权限，停止操作。");
                }
                return;
            }
            var acl=new DirectorySecurity(); acl.SetAccessRuleProtection(true,false);
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid,null));
            foreach(var sid in new[]{WellKnownSidType.BuiltinAdministratorsSid,WellKnownSidType.LocalSystemSid})
                acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid,null),FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(Sid),FileSystemRights.ReadAndExecute,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));
            Directory.CreateDirectory(Root,acl);
        }
        public static Enrollment Load() {
            if(!File.Exists(StateFile)) return null;
            if((File.GetAttributes(StateFile)&FileAttributes.ReparsePoint)!=0) throw new IOException("设置文件位置异常。");
            var e=Json.Read<Enrollment>(File.ReadAllText(StateFile,Encoding.UTF8));
            if(e==null || (e.Schema!=1 && e.Schema!=2)) throw new InvalidOperationException("设置格式版本不支持。");
            if(e.BaselineServices==null) e.BaselineServices=new System.Collections.Generic.List<string>();
            return e;
        }
        public static void Save(Enrollment e) {
            Prepare(); string tmp=Path.Combine(Root,"state-"+Guid.NewGuid().ToString("N")+".tmp");
            File.WriteAllText(tmp,Json.Write(e),Encoding.UTF8);
            if(File.Exists(StateFile)) File.Replace(tmp,StateFile,null); else File.Move(tmp,StateFile);
        }
        public static void Audit(string message) {
            Prepare(); string path=Path.Combine(Root,"activity-"+DateTime.UtcNow.ToString("yyyy-MM")+".log");
            if(File.Exists(path)&&new FileInfo(path).Length>2*1024*1024) return;
            File.AppendAllText(path,DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine,Encoding.UTF8);
        }
    }
    public interface IValueStore { RegistryValue Read(string key,string name); void Write(RegistryValue value); }
    public sealed class WindowsValues : IValueStore {
        public RegistryValue Read(string key,string name) {
            RegistryValue r=new RegistryValue { Key=key,Name=name,Exists=false };
            using(var k=Registry.LocalMachine.OpenSubKey(key)) {
                if(k==null || Array.IndexOf(k.GetValueNames(),name)<0) return r;
                r.Exists=true; r.Kind=k.GetValueKind(name).ToString(); object value=k.GetValue(name);
                r.Value=value is byte[] ? Convert.ToBase64String((byte[])value) : Convert.ToString(value,System.Globalization.CultureInfo.InvariantCulture);
            } return r;
        }
        public void Write(RegistryValue r) {
            using(var k=Registry.LocalMachine.OpenSubKey(r.Key,true)) {
                if(k==null) throw new InvalidOperationException("原注册表位置已不存在："+r.Key);
                if(!r.Exists) { k.DeleteValue(r.Name,false); return; }
                RegistryValueKind kind=(RegistryValueKind)Enum.Parse(typeof(RegistryValueKind),r.Kind);
                object value=r.Value; if(kind==RegistryValueKind.Binary) value=Convert.FromBase64String(r.Value);
                else if(kind==RegistryValueKind.DWord) value=Int32.Parse(r.Value);
                else throw new InvalidOperationException("不允许写入此类型。");
                k.SetValue(r.Name,value,kind);
            }
        }
    }
    public static class Changes {
        public static bool Same(RegistryValue a,RegistryValue b) { return a.Exists==b.Exists && (!a.Exists || (a.Kind==b.Kind&&a.Value==b.Value)); }
        public static bool Restore(IValueStore store,RegistryChange change) {
            RegistryValue now=store.Read(change.Before.Key,change.Before.Name);
            if(Same(now,change.Before)) return true;
            if(!Same(now,change.Applied)) return false;
            store.Write(change.Before); return true;
        }
    }
}
