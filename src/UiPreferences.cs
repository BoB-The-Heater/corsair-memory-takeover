using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CorsairTakeover {
    public sealed class UiPreferences {
        public bool HideTaskbar { get; set; }
        public bool ExitAfterConnect { get; set; }

        public UiPreferences() {
            HideTaskbar=false;
            ExitAfterConnect=true;
        }
    }

    public sealed class UiPreferencesStore {
        public string FilePath { get; private set; }

        public UiPreferencesStore() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CorsairTakeoverAssistant","preferences.json")) { }

        public UiPreferencesStore(string path) {
            if(String.IsNullOrWhiteSpace(path)) throw new ArgumentException("设置文件路径不能为空。","path");
            FilePath=Path.GetFullPath(path);
        }

        public UiPreferences Load() {
            string json;
            try { json=File.ReadAllText(FilePath,Encoding.UTF8); }
            catch(FileNotFoundException) { return new UiPreferences(); }
            catch(DirectoryNotFoundException) { return new UiPreferences(); }

            object decoded;
            try { decoded=new JavaScriptSerializer().DeserializeObject(json); }
            catch(ArgumentException ex) { throw new InvalidDataException("界面设置不是有效的 JSON。",ex); }
            catch(InvalidOperationException ex) { throw new InvalidDataException("界面设置不是有效的 JSON。",ex); }

            var values=decoded as Dictionary<string,object>;
            if(values==null) throw new InvalidDataException("界面设置必须是 JSON 对象。");
            object version;
            if(!values.TryGetValue("version",out version)||!(version is int)||(int)version!=1) throw new InvalidDataException("界面设置版本缺失或不受支持。");
            return new UiPreferences {
                HideTaskbar=ReadBoolean(values,"hideTaskbar"),
                ExitAfterConnect=ReadBoolean(values,"exitAfterConnect")
            };
        }

        static bool ReadBoolean(Dictionary<string,object> values,string name) {
            object value;
            if(!values.TryGetValue(name,out value)||!(value is bool)) throw new InvalidDataException("界面设置字段 "+name+" 缺失或不是布尔值。");
            return (bool)value;
        }

        public void Save(UiPreferences preferences) {
            if(preferences==null) throw new ArgumentNullException("preferences");
            string json=new JavaScriptSerializer().Serialize(new Dictionary<string,object> {
                { "version",1 },
                { "hideTaskbar",preferences.HideTaskbar },
                { "exitAfterConnect",preferences.ExitAfterConnect }
            });
            string directory=Path.GetDirectoryName(FilePath);
            Directory.CreateDirectory(directory);
            string temporaryPath=Path.Combine(directory,Path.GetFileName(FilePath)+"."+Guid.NewGuid().ToString("N")+".tmp");
            try {
                byte[] bytes=Encoding.UTF8.GetBytes(json);
                using(var file=new FileStream(temporaryPath,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                    file.Write(bytes,0,bytes.Length);
                    file.Flush(true);
                }
                if(File.Exists(FilePath)) File.Replace(temporaryPath,FilePath,null);
                else File.Move(temporaryPath,FilePath);
            }
            catch(Exception saveError) {
                try { File.Delete(temporaryPath); }
                catch(Exception cleanupError) { throw new IOException("界面设置保存失败，临时文件也无法删除。",new AggregateException(saveError,cleanupError)); }
                throw;
            }
        }
    }
}
