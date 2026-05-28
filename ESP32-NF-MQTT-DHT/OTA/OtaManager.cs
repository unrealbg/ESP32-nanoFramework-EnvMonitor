namespace ESP32_NF_MQTT_DHT.OTA
{
    using System;
    using System.IO;
    using System.Net.Http;
    using System.Reflection;
    using System.Text;
    using System.Threading;

    using nanoFramework.Runtime.Native;

    internal sealed class OtaManager
    {
        public void CheckAndUpdateFromUrl(string manifestUrl)
        {
            if (string.IsNullOrEmpty(manifestUrl))
            {
                return;
            }

            OtaUtil.SafeStatus("CHECKING", manifestUrl);
            var json = HttpGetString(manifestUrl);
            if (json == null)
            {
                throw new Exception("manifest download failed");
            }

            var mf = OtaManifest.Parse(json);
            if (mf == null || mf.Files == null || mf.Files.Length == 0)
            {
                throw new Exception("manifest parse failed or empty files");
            }

            if (!NeedUpdate(mf.Version))
            {
                OtaUtil.SafeStatus("UPTODATE", mf.Version);
                return;
            }

            // Step 1: download + verify all, write with backup; track updated paths for possible rollback
            string[] updated = new string[mf.Files.Length];
            int updatedCount = 0;

            for (int i = 0; i < mf.Files.Length; i++)
            {
                var f = mf.Files[i];
                string path = Combine(Config.AppDir, f.Name);
                string bak = path + ".bak";

                OtaUtil.SafeStatus("DOWNLOADING", f.Url);
                var data = HttpGetBytes(f.Url);
                if (data == null || data.Length == 0)
                {
                    throw new Exception("download empty: " + f.Name);
                }

                OtaUtil.SafeStatus("VERIFYING", f.Name);
                if (!OtaCrypto.VerifySha256Hex(data, f.Sha256))
                {
                    // Extra diagnostics to help pinpoint mismatch root causes
                    try
                    {
                        var actualHex = OtaCrypto.ToHexLower(Sha256Lite.ComputeHash(data));
                        Console.WriteLine("[OTA] SHA mismatch for " + f.Name);
                        Console.WriteLine("[OTA] expected: " + f.Sha256);
                        Console.WriteLine("[OTA] actual  : " + actualHex);
                        Console.WriteLine("[OTA] size    : " + data.Length);
                    }
                    catch
                    {
                        // ignored
                    }

                    throw new Exception("sha256 mismatch: " + f.Name);
                }

                EnsureDir(Config.AppDir);
                var parent = ParentDir(path);
                if (parent != null)
                {
                    EnsureDir(parent);
                }

                SafeWrite(path, bak, data);
                updated[updatedCount++] = path;
                OtaUtil.SafeStatus("WRITTEN", f.Name);
            }

            // Detect if manifest includes main app
            bool hasMainApp = false;
            for (int i = 0; i < mf.Files.Length; i++)
            {
                if (StringsEqual(mf.Files[i].Name, Config.MainAppName))
                {
                    hasMainApp = true;
                    break;
                }
            }

            // Step 2: load dependencies (every file except main app)
            bool depOk = true;
            for (int i = 0; i < mf.Files.Length; i++)
            {
                var f = mf.Files[i];
                if (StringsEqual(f.Name, Config.MainAppName))
                {
                    continue;
                }

                string p = Combine(Config.AppDir, f.Name);
                if (!TryLoad(p))
                {
                    depOk = false;
                    Console.WriteLine("[OTA] Dep load failed: " + f.Name);
                    break;
                }
            }

            // Step 3: load + start main app if present; otherwise accept module-only updates
            bool appOk = depOk;
            if (depOk && hasMainApp)
            {
                appOk = TryLoadAndStart(Combine(Config.AppDir, Config.MainAppName));
            }

            if (!appOk)
            {
                // rollback all updated files
                RollbackMany(updated, updatedCount);
                throw new Exception("load failed — rolled back");
            }

            WriteVersion(mf.Version);
            OtaUtil.SafeStatus("APPLIED", mf.Version);

            // Optional cleanup of old PE files and .bak leftovers
            if (Config.CleanAfterApply)
            {
                // Build keep list from manifest + always keep main app name
                string[] keep = new string[mf.Files.Length + 1];
                for (int i = 0; i < mf.Files.Length; i++)
                {
                    keep[i] = mf.Files[i].Name;
                }

                keep[mf.Files.Length] = Config.MainAppName;
                CleanOldFiles(Config.AppDir, keep, keep.Length);
                DeleteBackups(updated, updatedCount);
            }

            if (Config.RebootAfterApply)
            {
                OtaUtil.SafeStatus("REBOOT", null);
                Thread.Sleep(800);
                Power.RebootDevice();
            }
        }

        private static bool NeedUpdate(string newV)
        {
            try
            {
                if (!File.Exists(Config.VersionFile))
                {
                    return true;
                }

                var cur = File.ReadAllText(Config.VersionFile);
                return OtaUtil.CmpVer(newV, cur) > 0;
            }
            catch
            {
                return true;
            }
        }

        private static void WriteVersion(string v)
        {
            try
            {
                File.WriteAllText(Config.VersionFile, v);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OTA] version write: " + ex.Message);
            }
        }

        private static void EnsureDir(string p)
        {
            if (!Directory.Exists(p))
            {
                Directory.CreateDirectory(p);
            }
        }

        private static string Combine(string a, string b)
        {
            if (a.Length == 0)
            {
                return b;
            }

            if (b.Length == 0)
            {
                return a;
            }

            if (a[a.Length - 1] == '/')
            {
                return a + b;
            }

            return a + "/" + b;
        }

        private static string ParentDir(string path)
        {
            if (path == null)
            {
                return null;
            }

            int i = path.Length - 1;
            while (i >= 0 && path[i] != '/' && path[i] != (char)92)
            {
                i--;
            }

            if (i <= 0)
            {
                return null;
            }

            return path.Substring(0, i);
        }

        private static void SafeWrite(string active, string backup, byte[] data)
        {
            if (File.Exists(active))
            {
                if (File.Exists(backup))
                {
                    File.Delete(backup);
                }

                File.Move(active, backup);
            }

            using (var fs = new FileStream(active, FileMode.Create, FileAccess.Write))
            {
                fs.Write(data, 0, data.Length);
            }
        }

        private static void RollbackMany(string[] updated, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string path = updated[i];
                string bak = path + ".bak";
                try
                {
                    if (File.Exists(bak))
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }

                        File.Move(bak, path);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[OTA] rollback: " + ex.Message);
                }
            }
        }

        private static void DeleteBackups(string[] updated, int count)
        {
            for (int i = 0; i < count; i++)
            {
                string bak = updated[i] + ".bak";
                try
                {
                    if (File.Exists(bak))
                    {
                        File.Delete(bak);
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }

        private static void CleanOldFiles(string dir, string[] keepNames, int keepCount)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    return;
                }

                var files = Directory.GetFiles(dir);
                if (files == null)
                {
                    return;
                }

                for (int i = 0; i < files.Length; i++)
                {
                    string path = files[i];
                    string name = FileName(path);

                    // delete all .bak
                    if (EndsWith(name, ".bak"))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch
                        {
                            // ignored
                        }

                        continue;
                    }

                    // delete .pe not present in keep list
                    if (EndsWith(name, ".pe") && !NameListContains(keepNames, keepCount, name))
                    {
                        try
                        {
                            File.Delete(path);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OTA] clean: " + ex.Message);
            }
        }

        private static bool NameListContains(string[] list, int count, string name)
        {
            for (int i = 0; i < count; i++)
            {
                if (StringsEqual(list[i], name))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FileName(string path)
        {
            if (path == null)
            {
                return null;
            }

            int i = path.Length - 1;
            // Walk back until we hit '/' or '\' (92) or start
            while (i >= 0 && path[i] != '/' && path[i] != (char)92)
            {
                i--;
            }

            return (i >= 0) ? path.Substring(i + 1, path.Length - (i + 1)) : path;
        }

        private static bool EndsWith(string s, string suffix)
        {
            if (s == null || suffix == null)
            {
                return false;
            }

            int n = s.Length, m = suffix.Length;
            if (m > n)
            {
                return false;
            }

            for (int i = 0; i < m; i++)
            {
                if (s[n - m + i] != suffix[i]) return false;
            }

            return true;
        }

        private static bool TryLoad(string pePath)
        {
            try
            {
                if (!File.Exists(pePath))
                {
                    return false;
                }

                var pe = File.ReadAllBytes(pePath);
                var asm = Assembly.Load(pe);
                return asm != null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OTA] load dep: " + ex.Message);
                return false;
            }
        }

        private static bool TryLoadAndStart(string pePath)
        {
            try
            {
                if (!File.Exists(pePath))
                {
                    return false;
                }

                var pe = File.ReadAllBytes(pePath);
                var asm = Assembly.Load(pe); // nanoFramework single‑arg overload
                if (asm == null)
                {
                    return false;
                }

                // Preferred: use configured type/method
                var entryTypeName = Config.EntryTypeName;
                var entryMethodName = Config.EntryMethodName;

                Type t = null;
                if (!string.IsNullOrEmpty(entryTypeName))
                {
                    t = asm.GetType(entryTypeName);
                }

                MethodInfo m = null;
                if (t != null)
                {
                    m = t.GetMethod(entryMethodName, BindingFlags.Public | BindingFlags.Static);
                }

                // Fallback: try a few common patterns and, as a last resort, scan types
                if (m == null)
                {
                    string[] typeCandidates = new string[] { "Entry", "App.Entry", "Program", "App" };
                    string[] methodCandidates = new string[] { entryMethodName, "Start", "Main" };

                    for (int i = 0; i < typeCandidates.Length && m == null; i++)
                    {
                        var tt = asm.GetType(typeCandidates[i]);
                        if (tt == null)
                        {
                            continue;
                        }

                        for (int j = 0; j < methodCandidates.Length && m == null; j++)
                        {
                            m = tt.GetMethod(methodCandidates[j], BindingFlags.Public | BindingFlags.Static);
                            if (m != null) { t = tt; break; }
                        }
                    }
                }

                if (m == null)
                {
                    // Last chance: scan all exported types for a public static Start()/Main()
                    try
                    {
                        var types = asm.GetTypes();
                        for (int i = 0; i < types.Length && m == null; i++)
                        {
                            var tt = types[i];
                            m = tt.GetMethod(entryMethodName, BindingFlags.Public | BindingFlags.Static);
                            if (m == null)
                            {
                                m = tt.GetMethod("Start", BindingFlags.Public | BindingFlags.Static);
                            }

                            if (m == null)
                            {
                                m = tt.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
                            }

                            if (m != null)
                            {
                                t = tt;
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }
                }

                if (m == null)
                {
                    Console.WriteLine("[OTA] Entry type/method not found. Set Config.EntryTypeName/EntryMethodName.");
                    return false;
                }

                m.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OTA] load/invoke: " + ex.Message);
                return false;
            }
        }

        private static string HttpGetString(string url)
        {
            var b = HttpGetBytes(url);
            return b == null ? null : new string(Encoding.UTF8.GetChars(b, 0, b.Length));
        }

        private static byte[] HttpGetBytes(string url)
        {
            HttpClient client = null;
            HttpResponseMessage response = null;

            try
            {
                client = new HttpClient();
                if (Startup.OtaRootCaCert != null)
                {
                    client.HttpsAuthentCert = Startup.OtaRootCaCert;
                }
                // Prefer TLS 1.2 for compatibility; enforce certificate validation
                client.SslProtocols = System.Net.Security.SslProtocols.Tls12;
                client.SslVerification = System.Net.Security.SslVerification.CertificateRequired;
                try
                {
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
                }
                catch
                {
                    // ignored
                }

                response = client.Get(url);
                if (response == null || response.Content == null)
                {
                    return null;
                }

                if ((int)response.StatusCode != 200)
                {
                    Console.WriteLine("[HTTP] Non-OK status: " + (int)response.StatusCode);
                    return null;
                }

                return response.Content.ReadAsByteArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HTTP] " + ex.Message);
                return null;
            }
            finally
            {
                if (response != null)
                {
                    try
                    {
                        response.Dispose();
                    }
                    catch
                    {
                    }
                }

                if (client != null)
                {
                    client.Dispose();
                }
            }
        }

        private static bool StringsEqual(string a, string b)
        {
            if (a == null && b == null)
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
