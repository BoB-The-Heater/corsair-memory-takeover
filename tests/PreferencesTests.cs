using System;
using System.IO;
using System.Text;
using CorsairTakeover;

public static class PreferencesTests {
    static int count;

    static void Assert(bool condition,string message) {
        if(!condition) throw new Exception("FAIL: "+message);
        count++;
        Console.WriteLine("PASS: "+message);
    }

    static void RejectInvalidSettings(UiPreferencesStore store,string json,string message) {
        File.WriteAllText(store.FilePath,json,Encoding.UTF8);
        bool rejected=false;
        try { store.Load(); }
        catch(InvalidDataException) { rejected=true; }
        Assert(rejected,message);
    }

    public static int RunAll() {
        count=0;
        string directory=Path.Combine(Path.GetTempPath(),"CorsairTakeoverPreferencesTests-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(directory,"preferences.json");
        Directory.CreateDirectory(directory);
        try {
            var defaultStore=new UiPreferencesStore();
            string expectedPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CorsairTakeoverAssistant","preferences.json");
            Assert(defaultStore.FilePath==Path.GetFullPath(expectedPath),"default location belongs to the current user's local application data");

            var store=new UiPreferencesStore(path);
            var preferences=store.Load();
            Assert(!preferences.HideTaskbar&&preferences.ExitAfterConnect,"missing file keeps the taskbar entry and enables exit after connection");
            Assert(!File.Exists(path),"loading defaults does not write a settings file");
            var missingDirectoryStore=new UiPreferencesStore(Path.Combine(directory,"missing","preferences.json"));
            preferences=missingDirectoryStore.Load();
            Assert(!preferences.HideTaskbar&&preferences.ExitAfterConnect&&!Directory.Exists(Path.GetDirectoryName(missingDirectoryStore.FilePath)),"missing directory uses defaults without creating it");

            foreach(bool hideTaskbar in new[]{false,true}) {
                foreach(bool exitAfterConnect in new[]{false,true}) {
                    store.Save(new UiPreferences { HideTaskbar=hideTaskbar,ExitAfterConnect=exitAfterConnect });
                    preferences=new UiPreferencesStore(path).Load();
                    Assert(preferences.HideTaskbar==hideTaskbar&&preferences.ExitAfterConnect==exitAfterConnect,"both preferences persist across a new store instance: "+hideTaskbar+" / "+exitAfterConnect);
                }
            }

            RejectInvalidSettings(store,"{broken","malformed JSON is an explicit settings error");
            RejectInvalidSettings(store,"","empty settings are an explicit error");
            RejectInvalidSettings(store,"null","null is not a settings object");
            RejectInvalidSettings(store,"[]","an array is not a settings object");
            RejectInvalidSettings(store,"{\"hideTaskbar\":false,\"exitAfterConnect\":true}","version is required");
            RejectInvalidSettings(store,"{\"version\":2,\"hideTaskbar\":false,\"exitAfterConnect\":true}","an unknown version cannot silently use defaults");
            RejectInvalidSettings(store,"{\"version\":\"1\",\"hideTaskbar\":false,\"exitAfterConnect\":true}","the version cannot be a coerced string");
            RejectInvalidSettings(store,"{\"version\":1,\"exitAfterConnect\":true}","hideTaskbar is required");
            RejectInvalidSettings(store,"{\"version\":1,\"hideTaskbar\":false}","exitAfterConnect is required");
            RejectInvalidSettings(store,"{\"version\":1,\"hideTaskbar\":\"false\",\"exitAfterConnect\":true}","hideTaskbar cannot be a coerced string");
            RejectInvalidSettings(store,"{\"version\":1,\"hideTaskbar\":0,\"exitAfterConnect\":true}","hideTaskbar cannot be a coerced number");
            RejectInvalidSettings(store,"{\"version\":1,\"hideTaskbar\":false,\"exitAfterConnect\":\"true\"}","exitAfterConnect cannot be a coerced string");
            RejectInvalidSettings(store,"{\"version\":1,\"hideTaskbar\":false,\"exitAfterConnect\":null}","exitAfterConnect cannot be null");

            store.Save(new UiPreferences { HideTaskbar=true,ExitAfterConnect=false });
            string previousJson=File.ReadAllText(path,Encoding.UTF8);
            bool failed=false;
            using(var lockedFile=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                try { store.Save(new UiPreferences { HideTaskbar=false,ExitAfterConnect=true }); }
                catch(IOException) { failed=true; }
                catch(UnauthorizedAccessException) { failed=true; }
            }
            Assert(failed,"a replacement blocked by an open file surfaces its save failure");
            Assert(File.ReadAllText(path,Encoding.UTF8)==previousJson,"failed replacement preserves the previous file bytes");
            preferences=store.Load();
            Assert(preferences.HideTaskbar&&!preferences.ExitAfterConnect,"failed replacement preserves the previous preferences");
            Assert(Directory.GetFiles(directory,"preferences.json.*.tmp").Length==0,"failed replacement removes its own temporary file");
            return count;
        }
        finally {
            File.Delete(path);
            foreach(string temporaryFile in Directory.GetFiles(directory,"preferences.json.*.tmp")) File.Delete(temporaryFile);
            Directory.Delete(directory);
        }
    }

    public static int Main() {
        try {
            Console.WriteLine("Preferences tests passed: "+RunAll());
            return 0;
        }
        catch(Exception ex) {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
