using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace PVZF_Launcher
{
    internal static class Program
    {
        internal static string ResourceDirectory;

        private const string GitHubOwner = "MrModification";
        private const string GitHubRepo = "PVZF-Launcher";
        private const string GitHubExeName = "PVZF_Launcher.exe";

        [STAThread]
        static void Main()
        {
            string procName = Process.GetCurrentProcess().ProcessName;
            int currentPid = Process.GetCurrentProcess().Id;

            foreach (var p in Process.GetProcessesByName(procName))
            {
                if (p.Id != currentPid)
                {
                    try
                    {
                        p.Kill();
                    }
                    catch { }
                }
            }

            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string exeName = Path.GetFileName(exePath);

            string baseDir;

            if (exeName.IndexOf("_P", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                baseDir = Path.GetDirectoryName(exePath);
            }
            else
            {
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PVZF_Launcher"
                );
            }

            ResourceDirectory = Path.Combine(baseDir, "PVZFL_Resources");
            Directory.CreateDirectory(ResourceDirectory);

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (args.Name.StartsWith("Newtonsoft.Json"))
                {
                    string dllPath = Path.Combine(ResourceDirectory, "Newtonsoft.Json.dll");
                    if (File.Exists(dllPath))
                        return Assembly.LoadFrom(dllPath);
                }
                return null;
            };

            string[] wavFiles = Directory.GetFiles(
                ResourceDirectory,
                "*.wav",
                SearchOption.TopDirectoryOnly
            );

            if (wavFiles.Length > 0)
            {
                try
                {
                    var player = new System.Media.SoundPlayer(wavFiles[0]);
                    player.Load();
                    player.PlayLooping();
                }
                catch { }
            }

            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show("Thread crash:\n\n" + e.Exception.ToString());
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                MessageBox.Show("Fatal crash:\n\n" + e.ExceptionObject.ToString());
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                BootstrapSelf();
            }
            catch { }

            try
            {
                Application.Run(new Launcher());
            }
            catch (Exception ex)
            {
                MessageBox.Show("Constructor crash:\n\n" + ex.ToString());
            }
        }
        private static void DeleteIfExists(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private static void CreateOrUpdateDesktopShortcut(string targetExe)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string shortcutPath = Path.Combine(desktop, "PVZF Launcher.lnk");

            DeleteIfExists(shortcutPath);
            CreateShortcut(shortcutPath, targetExe);
        }

        private static void CreateOrUpdateStartMenuShortcut(string targetExe)
        {
            string startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
            string shortcutPath = Path.Combine(startMenu, "PVZF Launcher.lnk");

            DeleteIfExists(shortcutPath);
            CreateShortcut(shortcutPath, targetExe);
        }

        private static void CreateShortcut(string shortcutPath, string targetExe)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(t);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        private static Version GetFileVersion(string path)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return new Version(info.FileVersion ?? "0.0.0.0");
            }
            catch
            {
                return new Version(0, 0, 0, 0);
            }
        }

        private static Version GetEmbeddedNewtonsoftVersion()
        {
            string temp = Path.GetTempFileName();
            File.WriteAllBytes(temp, Properties.Resources.Newtonsoft_Json);

            Version v = GetFileVersion(temp);

            File.Delete(temp);
            return v;
        }

        private static string EnsureResourceFolder(string baseDir)
        {
            string resDir = Path.Combine(baseDir, "PVZFL_Resources");

            if (!Directory.Exists(resDir))
                Directory.CreateDirectory(resDir);

            return resDir;
        }

        private static void UnpackNewtonsoft(string baseDir)
        {
            string resDir = EnsureResourceFolder(baseDir);
            string dllPath = Path.Combine(resDir, "Newtonsoft.Json.dll");

            if (!File.Exists(dllPath))
            {
                File.WriteAllBytes(dllPath, Properties.Resources.Newtonsoft_Json);
                return;
            }

            Version embedded = GetEmbeddedNewtonsoftVersion();
            Version existing = GetFileVersion(dllPath);

            if (existing < embedded)
                File.WriteAllBytes(dllPath, Properties.Resources.Newtonsoft_Json);
        }
        private static void UnpackWav_KeepExisting(byte[] data, string outputPath)
        {
            try
            {
                if (!File.Exists(outputPath))
                    File.WriteAllBytes(outputPath, data);
            }
            catch { }
        }

        private static void UnpackJson_AlwaysOverwrite(string text, string outputPath)
        {
            try
            {
                File.WriteAllText(outputPath, text);
            }
            catch { }
        }

        private static void UnpackStaticResources(string baseDir)
        {
            string resDir = EnsureResourceFolder(baseDir);

            string wavPath = Path.Combine(resDir, "Launcher_Music.wav");
            string jsonPath = Path.Combine(resDir, "InstallationStore.json");

            UnpackWav_KeepExisting(Properties.Resources.Launcher_Music, wavPath);

            UnpackJson_AlwaysOverwrite(Properties.Resources.InstallationStore, jsonPath);
        }
        private static async Task<(Version version, string downloadUrl)> GetLatestGitHubReleaseAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PVZF-Launcher");

                    string url =
                        $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
                    string json = await client.GetStringAsync(url).ConfigureAwait(false);

                    JObject obj = JObject.Parse(json);
                    string tag = obj["tag_name"]?.ToString() ?? "0.0.0.0";

                    Version version;
                    if (!Version.TryParse(tag.TrimStart('v', 'V'), out version))
                        version = new Version(0, 0, 0, 0);

                    string downloadUrl = null;

                    var assets = obj["assets"] as JArray;
                    if (assets != null)
                    {
                        foreach (var asset in assets)
                        {
                            string name = asset["name"]?.ToString();
                            if (
                                string.Equals(
                                    name,
                                    GitHubExeName,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                downloadUrl = asset["browser_download_url"]?.ToString();
                                break;
                            }
                        }
                    }

                    return (version, downloadUrl);
                }
            }
            catch
            {
                return (new Version(0, 0, 0, 0), null);
            }
        }

        private static void CheckAndLaunchGitHubUpdate(string targetDir, string targetExe)
        {
            Splash splash = new Splash("Checking for updates...");
            splash.Show();
            try
            {
                var latest = GetLatestGitHubReleaseAsync().GetAwaiter().GetResult();
                if (latest.version == null || string.IsNullOrEmpty(latest.downloadUrl))
                    return;

                Version running = GetFileVersion(targetExe);

                if (latest.version <= running)
                    return;

                string updatePath = Path.Combine(targetDir, "PVZF_Launcher_Update.exe");

                using (var client = new HttpClient())
                {
                    var data = client
                        .GetByteArrayAsync(latest.downloadUrl)
                        .GetAwaiter()
                        .GetResult();
                    File.WriteAllBytes(updatePath, data);
                }
                splash.UpdateMessage("Launching updater...");
                splash.Refresh();
                Process.Start(updatePath);
                Environment.Exit(0);
            }
            catch { }
            splash.Close();
        }
        private static void BootstrapSelf()
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string exeName = Path.GetFileName(exePath);

            if (exeName.IndexOf("_P", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string dir = Path.GetDirectoryName(exePath);
                UnpackNewtonsoft(dir);
                UnpackStaticResources(dir);
                return;
            }

            string targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PVZF_Launcher"
            );

            string targetExe = Path.Combine(targetDir, "PVZF_Launcher.exe");

            if (string.Equals(exePath, targetExe, StringComparison.OrdinalIgnoreCase))
            {
                UnpackNewtonsoft(targetDir);
                UnpackStaticResources(targetDir);

                CheckAndLaunchGitHubUpdate(targetDir, targetExe);
                return;
            }

            bool dirJustCreated = false;

            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
                dirJustCreated = true;
            }

            Splash splash = new Splash("Preparing launcher...");
            splash.Show();
            Application.DoEvents();

            UnpackNewtonsoft(targetDir);
            UnpackStaticResources(targetDir);

            if (File.Exists(targetExe))
            {
                Version installed = GetFileVersion(targetExe);
                Version running = GetFileVersion(exePath);

                if (running > installed)
                {
                    splash.UpdateMessage("Updating launcher...");
                    File.Copy(exePath, targetExe, true);

                    CreateOrUpdateDesktopShortcut(targetExe);
                    CreateOrUpdateStartMenuShortcut(targetExe);
                }

                splash.UpdateMessage("Launching...");
                Process.Start(targetExe);
                splash.Close();
                Environment.Exit(0);
            }
            else
            {
                splash.UpdateMessage("Installing launcher...");
                File.Copy(exePath, targetExe, true);

                if (dirJustCreated)
                {
                    splash.UpdateMessage("Creating shortcuts...");
                    CreateOrUpdateDesktopShortcut(targetExe);
                    CreateOrUpdateStartMenuShortcut(targetExe);
                }

                splash.UpdateMessage("Launching...");
                Process.Start(targetExe);
                splash.Close();
                Environment.Exit(0);
            }
        }
    }
}