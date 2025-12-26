using Newtonsoft.Json;
using PVZF_Launcher.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;

namespace PVZF_Launcher
{
    public class Launcher : Form
    {
        private Label lblVersion;
        private Label lblDirectory;
        private Button btnBrowse;
        private Button btnPatch;
        private Button btnLaunch;
        private Button btnQuit;

        private Panel installPanel;

        private string currentDirectory = "";

        private string installListFile;

        private List<InstallationInfo> installPaths = new List<InstallationInfo>();

        public class InstallationInfo
        {
            public string Path { get; set; }
            public string ExeName { get; set; }
            public int ModCount { get; set; }
            public bool IsPatched { get; set; }
            public DateTime LastPlayed { get; set; } = DateTime.MinValue;
            public string LoaderType { get; set; } = "MelonLoader";

            private string _displayName;

            public string DisplayName
            {
                get
                {
                    if (!string.IsNullOrWhiteSpace(_displayName))
                        return _displayName;

                    try
                    {
                        return System.IO.Path.GetFileName(
                            Path.TrimEnd(
                                System.IO.Path.DirectorySeparatorChar,
                                System.IO.Path.AltDirectorySeparatorChar
                            )
                        );
                    }
                    catch
                    {
                        return Path;
                    }
                }
                set { _displayName = value; }
            }
        }

        private void StyleButton(Button btn)
        {
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 70, 70);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(50, 50, 50);
            btn.BackColor = Color.FromArgb(60, 60, 60);
            btn.ForeColor = Color.White;
            btn.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        }

        private string ExtractExeDirectoryFromLogLine(string line)
        {
            const string marker = "Loading player data from ";
            if (!line.Contains(marker))
                return "";

            string fullPath = line.Split(new[] { marker }, StringSplitOptions.None)[1];

            fullPath = fullPath.Replace("/", "\\");

            int dataIndex = fullPath.IndexOf("_Data\\", StringComparison.OrdinalIgnoreCase);
            if (dataIndex < 0)
                return "";

            string dataFolder = fullPath.Substring(0, dataIndex + "_Data".Length);

            string exeDir = Directory.GetParent(dataFolder)?.FullName;

            return exeDir ?? "";
        }

        private static string EnsureResourceFolder(string baseDir)
        {
            string resDir = Path.Combine(baseDir, "PVZFL_Resources");

            if (!Directory.Exists(resDir))
                Directory.CreateDirectory(resDir);

            return resDir;
        }

        private static void UnpackBackground(string baseDir)
        {
            string resDir = EnsureResourceFolder(baseDir);
            string imagePath = Path.Combine(resDir, "Background.png");

            if (!File.Exists(imagePath))
            {
                File.WriteAllBytes(imagePath, Properties.Resources.Background);
                return;
            }
        }

        private void LoadBackgroundImage()
        {
            string resDir = Path.Combine(currentDirectory, "PVZFL_Resources");
            string imagePath = Path.Combine(resDir, "Background.png");

            if (!File.Exists(imagePath))
                UnpackBackground(currentDirectory);
            try
            {
                this.BackgroundImage = Image.FromFile(imagePath);
                this.BackgroundImageLayout = ImageLayout.Stretch;
            }
            catch
            {
                this.BackgroundImage = null;
            }
        }
        private void LoadIcon()
        {
            this.Icon = Properties.Resources.app;
        }
        public Launcher()
        {
            InitializeComponent();
            LoadBackgroundImage();
            LoadIcon();

            string resDir = EnsureResourceFolder(AppDomain.CurrentDomain.BaseDirectory);
            installListFile = Path.Combine(resDir, "installations.json");

            LoadInstallPaths();
            AutoDetectAndRegisterInstallation();
            SelectDefaultInstallation();
            UpdateBrowseButtonText();
            UpdatePatchButtonState();
        }

        private void InitializeComponent()
        {
            this.lblVersion = new Label();
            this.lblDirectory = new Label();
            this.btnBrowse = new Button();
            this.btnPatch = new Button();
            this.btnLaunch = new Button();
            this.btnQuit = new Button();

            this.SuspendLayout();

            this.lblVersion.AutoSize = true;
            this.lblVersion.ForeColor = Color.Gray;
            this.lblVersion.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            this.lblVersion.Text = $"v{Application.ProductVersion}";
            this.lblDirectory.Location = new Point(5, 90);
            this.lblVersion.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.Controls.Add(this.lblVersion);

            this.lblDirectory.AutoSize = true;
            this.lblDirectory.ForeColor = Color.White;
            this.lblDirectory.Location = new Point(5, 30);
            this.lblDirectory.Name = "lblDirectory";
            this.lblDirectory.Size = new Size(107, 13);
            this.lblDirectory.TabIndex = 0;
            this.lblDirectory.Text = "No directory selected";

            this.btnBrowse.Location = new Point(475, 75);
            this.btnBrowse.Name = "btnBrowse";
            this.btnBrowse.TabIndex = 1;
            this.btnBrowse.Text = "Change Directory";
            this.btnBrowse.Click += new EventHandler(this.btnBrowse_Click);
            this.btnBrowse.Height = 35;
            this.btnBrowse.Width = 150;

            this.btnPatch.Location = new Point(475, 125);
            this.btnPatch.Name = "btnPatch";
            this.btnPatch.TabIndex = 2;
            this.btnPatch.Text = "Patch Game";
            this.btnPatch.Click += new EventHandler(this.btnPatch_Click);
            this.btnPatch.Height = 35;
            this.btnPatch.Width = 150;

            this.btnLaunch.Location = new Point(475, 175);
            this.btnLaunch.Name = "btnLaunch";
            this.btnLaunch.TabIndex = 3;
            this.btnLaunch.Text = "Launch Game";
            this.btnLaunch.Click += new EventHandler(this.btnLaunch_Click);
            this.btnLaunch.Height = 35;
            this.btnLaunch.Width = 150;

            this.btnQuit.Location = new Point(475, 225);
            this.btnQuit.Name = "btnQuit";
            this.btnQuit.TabIndex = 4;
            this.btnQuit.Text = "Quit Launcher";
            this.btnQuit.Click += new EventHandler(this.btnQuit_Click);
            this.btnQuit.Height = 35;
            this.btnQuit.Width = 150;

            StyleButton(btnBrowse);
            StyleButton(btnPatch);
            StyleButton(btnLaunch);
            StyleButton(btnQuit);

            this.BackColor = Color.FromArgb(40, 40, 40);
            this.ClientSize = new Size(750, 350);
            this.Controls.Add(this.lblDirectory);
            this.Controls.Add(this.btnBrowse);
            this.Controls.Add(this.btnPatch);
            this.Controls.Add(this.btnLaunch);
            this.Controls.Add(this.btnQuit);
            this.FormBorderStyle = FormBorderStyle.None;
            this.Name = "PVZFLAUNCHER";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "PvZ RH Mod Installer";
            this.Load += new EventHandler(this.Form1_Load);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                    m.Result = (IntPtr)HTCAPTION;
                return;
            }

            base.WndProc(ref m);
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(currentDirectory))
                lblDirectory.Text = currentDirectory;
        }

        private string AutoDetectGameDirectoryFromLog()
        {
            try
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"..\LocalLow\LanPiaoPiao\PlantsVsZombiesRH\Player.log"
                );

                logPath = Path.GetFullPath(logPath);

                if (!File.Exists(logPath))
                    return "";

                foreach (string line in File.ReadAllLines(logPath))
                {
                    if (!line.Contains("Loading player data from"))
                        continue;

                    string exeDir = ExtractExeDirectoryFromLogLine(line);
                    if (!string.IsNullOrEmpty(exeDir))
                        return exeDir;
                }
            }
            catch { }

            return "";
        }

        private void ExtractZipOverwrite(string zipPath, string outputDir)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string filePath = Path.Combine(outputDir, entry.FullName);

                    string dir = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    entry.ExtractToFile(filePath, true);
                }
            }
        }

        private string GetFusionInstallRoot()
        {
            var store = LoadInstallationStore();
            string root = ResolveRootPath(store.RootPath);

            if (!Directory.Exists(root))
                Directory.CreateDirectory(root);

            return root;
        }
        private bool IsValidVersionZip(string versionZip)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(versionZip))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (
                            entry.Name.Equals(
                                "PlantsVsZombiesRH.exe",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private bool IsValidNet6Zip(string net6Zip)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(net6Zip))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (
                            entry.FullName.EndsWith(
                                "Il2CppInterop.Common.dll",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            string parent = Path.GetDirectoryName(entry.FullName);
                            if (parent != null)
                            {
                                parent = parent.Replace("\\", "/");
                                string[] parts = parent.Split('/');
                                string folderName = parts[parts.Length - 1];

                                if (folderName == "net6")
                                    return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private bool IsValidTranslationModZip(string translationZip)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(translationZip))
                {
                    foreach (var entry in zip.Entries)
                    {
                        if (
                            entry.FullName.EndsWith(
                                "PvZ_Fusion_Translator.dll",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }
        private bool ExtractVersionZip(string zipPath, string installPath)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    string gameRoot = null;

                    foreach (var entry in zip.Entries)
                    {
                        if (
                            entry.Name.Equals(
                                "PlantsVsZombiesRH.exe",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            string parent = Path.GetDirectoryName(entry.FullName);
                            if (parent == null)
                                parent = "";

                            parent = parent.Replace("\\", "/");
                            gameRoot = parent;
                            break;
                        }
                    }

                    if (gameRoot == null)
                        return false;

                    bool inRoot = gameRoot.Length == 0;

                    foreach (var entry in zip.Entries)
                    {
                        string full = entry.FullName.Replace("\\", "/");

                        if (!inRoot)
                        {
                            if (
                                !full.StartsWith(gameRoot + "/", StringComparison.OrdinalIgnoreCase)
                                && !full.Equals(gameRoot, StringComparison.OrdinalIgnoreCase)
                            )
                                continue;
                        }

                        string relativePath = inRoot
                            ? full.TrimStart('/', '\\')
                            : full.Substring(gameRoot.Length).TrimStart('/', '\\');

                        if (relativePath.Length == 0)
                            continue;

                        string outPath = Path.Combine(installPath, relativePath);

                        if (full.EndsWith("/"))
                        {
                            if (!Directory.Exists(outPath))
                                Directory.CreateDirectory(outPath);
                        }
                        else
                        {
                            string dir = Path.GetDirectoryName(outPath);
                            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                                Directory.CreateDirectory(dir);

                            entry.ExtractToFile(outPath, true);
                        }
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void InstallNet6(string net6Zip, string installPath)
        {
            string net6FolderPath = null;

            using (ZipArchive zip = ZipFile.OpenRead(net6Zip))
            {
                foreach (var entry in zip.Entries)
                {
                    if (
                        entry.FullName.EndsWith(
                            "Il2CppInterop.Common.dll",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        string parent = Path.GetDirectoryName(entry.FullName);
                        if (parent != null)
                        {
                            parent = parent.Replace("\\", "/");
                            string[] parts = parent.Split('/');
                            string folderName = parts[parts.Length - 1];

                            if (folderName == "net6")
                            {
                                net6FolderPath = parent;
                                break;
                            }
                        }
                    }
                }

                if (net6FolderPath == null)
                    throw new InvalidOperationException(
                        "Invalid net6.zip – required DLL not found inside 'net6' folder."
                    );

                string melonFolder = Path.Combine(installPath, "MelonLoader");
                if (!Directory.Exists(melonFolder))
                    Directory.CreateDirectory(melonFolder);

                foreach (var entry in zip.Entries)
                {
                    string full = entry.FullName.Replace("\\", "/");

                    if (!full.StartsWith(net6FolderPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string relative = full.Substring(net6FolderPath.Length).TrimStart('/', '\\');
                    if (relative.Length == 0)
                        continue;

                    string outPath = Path.Combine(melonFolder, "net6", relative);

                    if (full.EndsWith("/"))
                    {
                        if (!Directory.Exists(outPath))
                            Directory.CreateDirectory(outPath);
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        entry.ExtractToFile(outPath, true);
                    }
                }
            }
        }

        private void InstallTranslationMod(string translationZip, string installPath)
        {
            string translatorFolderPath = null;

            using (ZipArchive zip = ZipFile.OpenRead(translationZip))
            {
                foreach (var entry in zip.Entries)
                {
                    if (
                        entry.FullName.EndsWith(
                            "PvZ_Fusion_Translator.dll",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        string parent = Path.GetDirectoryName(entry.FullName);
                        if (parent == null)
                            parent = "";

                        translatorFolderPath = parent.Replace("\\", "/");
                        break;
                    }
                }

                if (translatorFolderPath == null)
                    throw new InvalidOperationException(
                        "Invalid TranslationMod.zip – PvZ_Fusion_Translator.dll not found."
                    );

                string modsFolder = Path.Combine(installPath, "Mods");
                if (!Directory.Exists(modsFolder))
                    Directory.CreateDirectory(modsFolder);

                foreach (var entry in zip.Entries)
                {
                    string full = entry.FullName.Replace("\\", "/");

                    if (!full.StartsWith(translatorFolderPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string relative = full.Substring(translatorFolderPath.Length)
                        .TrimStart('/', '\\');
                    if (relative.Length == 0)
                        continue;

                    string outPath = Path.Combine(modsFolder, relative);

                    if (full.EndsWith("/"))
                    {
                        if (!Directory.Exists(outPath))
                            Directory.CreateDirectory(outPath);
                    }
                    else
                    {
                        string dir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        entry.ExtractToFile(outPath, true);
                    }
                }
            }
        }

        private void CreateNewInstallationFromLocalZip(
            string version,
            string loader,
            string zipPath,
            string modpackZipPath,
            string language,
            string selectedNet6Zip,
            string selectedTranslationZip
        )
        {
            try
            {
                string root = GetFusionInstallRoot();
                string installPath = Path.Combine(root, version);

                if (!Directory.Exists(installPath))
                    Directory.CreateDirectory(installPath);

                if (!ExtractVersionZip(zipPath, installPath))
                {
                    MessageBox.Show(
                        "Failed to extract version files.\nPlantsVsZombiesRH.exe not found.",
                        "Installation Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return;
                }

                if (loader == "MelonLoader")
                {
                    ExtractMelonLoader(installPath);
                }
                else if (loader == "BepInEx")
                {
                    ExtractBepInEx(installPath);
                }

                if (!string.IsNullOrEmpty(modpackZipPath))
                {
                    ExtractZipOverwrite(modpackZipPath, installPath);
                }

                bool languageChanged = false;

                if (loader == "MelonLoader" && language != "Chinese")
                {
                    string userData = Path.Combine(installPath, "UserData");
                    string cfgPath = Path.Combine(userData, "MelonPreferences.cfg");

                    if (!Directory.Exists(userData))
                        Directory.CreateDirectory(userData);

                    if (File.Exists(cfgPath))
                    {
                        List<string> lines = File.ReadAllLines(cfgPath).ToList();
                        int index = lines.FindIndex(
                            delegate (string l)
                            {
                                return l.Trim() == "[PvZ_Fusion_Translator]";
                            }
                        );

                        if (index != -1)
                        {
                            for (int i = index + 1; i < lines.Count; i++)
                            {
                                string line = lines[i];

                                if (line.StartsWith("[") && line.EndsWith("]"))
                                    break;

                                if (line.StartsWith("Language"))
                                {
                                    string[] parts = line.Split('=');
                                    if (parts.Length == 2)
                                    {
                                        string oldLang = parts[1].Trim().Trim('"');
                                        if (oldLang != language)
                                            languageChanged = true;
                                    }

                                    lines[i] = "Language = \"" + language + "\"";
                                }
                            }
                        }
                        else
                        {
                            languageChanged = true;

                            lines.Add("");
                            lines.Add("[PvZ_Fusion_Translator]");
                            lines.Add("DefaultTextures = false");
                            lines.Add("DefaultAudio = false");
                            lines.Add("Language = \"" + language + "\"");
                        }

                        File.WriteAllLines(cfgPath, lines);
                    }
                    else
                    {
                        languageChanged = true;

                        File.WriteAllLines(
                            cfgPath,
                            new string[]
                            {
                                "[PvZ_Fusion_Translator]",
                                "DefaultTextures = false",
                                "DefaultAudio = false",
                                "Language = \"" + language + "\""
                            }
                        );
                    }

                    if (languageChanged)
                    {
                        if (
                            string.IsNullOrEmpty(selectedNet6Zip)
                            || !IsValidNet6Zip(selectedNet6Zip)
                        )
                        {
                            MessageBox.Show(
                                "net6.zip is missing or invalid.\nCannot install Translation Mod support.",
                                "Translation Mod Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error
                            );
                            return;
                        }

                        if (
                            string.IsNullOrEmpty(selectedTranslationZip)
                            || !IsValidTranslationModZip(selectedTranslationZip)
                        )
                        {
                            MessageBox.Show(
                                "TranslationMod.zip is missing or invalid.\nCannot install Translation Mod support.",
                                "Translation Mod Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error
                            );
                            return;
                        }

                        InstallNet6(selectedNet6Zip, installPath);
                        InstallTranslationMod(selectedTranslationZip, installPath);
                    }
                }

                bool exists = false;
                foreach (var i in installPaths)
                {
                    if (string.Equals(i.Path, installPath, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    var info = CreateInstallationInfo(installPath);
                    installPaths.Add(info);
                    SaveInstallPaths();
                }

                MessageBox.Show("Installation completed successfully!");
                RebuildMainUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Installation failed:\n" + ex.Message);
            }
        }
        private void ExtractMelonLoader(string installDir)
        {
            string temp = Path.Combine(installDir, "ml.zip");
            File.WriteAllBytes(temp, Properties.Resources.PrePackedMelon);
            ExtractZipOverwrite(temp, installDir);
            File.Delete(temp);
        }

        private void ExtractBepInEx(string installDir)
        {
            string temp = Path.Combine(installDir, "bepinex.zip");
            File.WriteAllBytes(temp, Properties.Resources.PrePackedBep);
            ExtractZipOverwrite(temp, installDir);
            File.Delete(temp);
        }

        private void AutoDetectAndRegisterInstallation()
        {
            string auto = AutoDetectGameDirectoryFromLog();
            if (string.IsNullOrEmpty(auto))
                return;

            if (
                !installPaths.Any(
                    i => string.Equals(i.Path, auto, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                var info = CreateInstallationInfo(auto);
                installPaths.Add(info);
                SaveInstallPaths();
            }
        }
        private void LoadInstallPaths()
        {
            try
            {
                if (File.Exists(installListFile))
                {
                    string json = File.ReadAllText(installListFile);
                    installPaths =
                        JsonConvert.DeserializeObject<List<InstallationInfo>>(json)
                        ?? new List<InstallationInfo>();
                }

                foreach (var inst in installPaths)
                    RefreshInstallationMetadata(inst);

                SaveInstallPaths();
            }
            catch
            {
                installPaths = new List<InstallationInfo>();
            }
        }

        private void SaveInstallPaths()
        {
            try
            {
                string json = JsonConvert.SerializeObject(installPaths, Formatting.Indented);
                File.WriteAllText(installListFile, json);
            }
            catch { }
        }

        private InstallationInfo CreateInstallationInfo(string path)
        {
            var info = new InstallationInfo { Path = path };
            RefreshInstallationMetadata(info);
            return info;
        }

        private void RefreshInstallationMetadata(InstallationInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.Path))
                return;

            try
            {
                string exe = Directory.Exists(info.Path)
                    ? Directory.GetFiles(info.Path, "*.exe").FirstOrDefault()
                    : null;
                info.ExeName = exe != null ? Path.GetFileName(exe) : "";

                info.IsPatched = IsPatched(info.Path);

                if (string.IsNullOrEmpty(info.LoaderType))
                {
                    string type = GetPatchType(info.Path);

                    if (type == "Melon")
                        info.LoaderType = "MelonLoader";
                    else if (type == "Bep")
                        info.LoaderType = "BepInEx";
                }

                info.ModCount = CountMods(info.Path, info.LoaderType);
            }
            catch
            {
                info.LoaderType = "";
                info.ExeName = "";
                info.ModCount = 0;
                info.IsPatched = false;
            }
        }

        private int CountMods(string installPath, string loaderType)
        {
            try
            {
                string modsDir;

                if (string.Equals(loaderType, "BepInEx", StringComparison.OrdinalIgnoreCase))
                {
                    modsDir = Path.Combine(installPath, "BepInEx", "Plugins");
                }
                else
                {
                    modsDir = Path.Combine(installPath, "Mods");
                }

                if (!Directory.Exists(modsDir))
                    return 0;

                var dllFiles = Directory
                    .GetFiles(modsDir, "*.dll", SearchOption.AllDirectories)
                    .Select(Path.GetFileName);

                if (string.Equals(loaderType, "BepInEx", StringComparison.OrdinalIgnoreCase))
                    return dllFiles.Count();

                var corePattern = new Regex(@"^_\d+_", RegexOptions.IgnoreCase);

                return dllFiles.Count(name => !corePattern.IsMatch(name));
            }
            catch
            {
                return 0;
            }
        }

        private bool IsPatched(string installPath)
        {
            try
            {
                return Directory.Exists(Path.Combine(installPath, "MelonLoader"))
                    || Directory.Exists(Path.Combine(installPath, "BepInEx"));
            }
            catch
            {
                return false;
            }
        }

        private string GetPatchType(string installPath)
        {
            try
            {
                if (Directory.Exists(Path.Combine(installPath, "MelonLoader")))
                    return "Melon";

                if (Directory.Exists(Path.Combine(installPath, "BepInEx")))
                    return "Bep";

                return "false";
            }
            catch
            {
                return "false";
            }
        }

        private void SelectDefaultInstallation()
        {
            if (installPaths.Count == 0)
            {
                currentDirectory = "";
                lblDirectory.Text = "No directory selected";
                return;
            }

            var best = installPaths
                .OrderByDescending(i => i.LastPlayed)
                .ThenBy(i => i.DisplayName)
                .First();

            currentDirectory = best.Path;
            lblDirectory.Text = currentDirectory;
        }

        public class InstallationStore
        {
            public string RootPath { get; set; }
            public string TranslationMod { get; set; }
            public List<StoreEntry> Versions { get; set; } = new List<StoreEntry>();
        }

        public class StoreEntry
        {
            public string Version { get; set; }
            public string DownloadUrl { get; set; }
        }

        private string ResolveRootPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(docs, "My Games", "PlantsVsZombiesFusionInstalls");
            }

            string docsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            return rawPath.Replace("%Documents%", docsFolder).Replace("/", "\\");
        }

        private bool InstallationStoreExists()
        {
            string resDir = EnsureResourceFolder(AppDomain.CurrentDomain.BaseDirectory);
            string storePath = Path.Combine(resDir, "InstallationStore.json");
            return File.Exists(storePath);
        }

        private InstallationStore LoadInstallationStore()
        {
            if (!InstallationStoreExists())
                return new InstallationStore();

            try
            {
                string resDir = EnsureResourceFolder(AppDomain.CurrentDomain.BaseDirectory);
                string storePath = Path.Combine(resDir, "InstallationStore.json");

                string json = File.ReadAllText(storePath);
                return JsonConvert.DeserializeObject<InstallationStore>(json)
                    ?? new InstallationStore();
            }
            catch
            {
                return new InstallationStore();
            }
        }

        private void UpdateBrowseButtonText()
        {
            bool storeExists = InstallationStoreExists();

            if (!storeExists)
            {
                if (installPaths.Count == 1)
                {
                    btnBrowse.Text = "Change Installation";
                    return;
                }
                else
                {
                    btnBrowse.Text = "Manage Installs";
                    return;
                }
            }

            if (installPaths.Count == 0)
            {
                btnBrowse.Text = "Create Installation";
            }
            else if (installPaths.Count == 1)
            {
                btnBrowse.Text = "Change Installation";
            }
            else
            {
                btnBrowse.Text = "Manage Installs";
            }
        }

        private void UpdatePatchButtonState()
        {
            if (string.IsNullOrEmpty(currentDirectory))
            {
                btnPatch.Text = "Patch Game";
                return;
            }

            bool patched = IsPatched(currentDirectory);
            btnPatch.Text = patched ? "Unpatch" : "Patch";
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            bool storeExists = InstallationStoreExists();
            if (!storeExists)
            {
                ShowInstallationManager();
                return;
            }

            if (installPaths.Count > 0)
            {
                ShowInstallationManager();
                return;
            }

            ShowCreateInstallationMenu(true);
        }

        private void ShowInlineEditor(InstallationInfo inst)
        {
            this.Controls.Clear();

            var title = new Label
            {
                Text = "Edit Installation",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };
            this.Controls.Add(title);

            var lblName = new Label
            {
                Text = "Display Name:",
                ForeColor = Color.White,
                Location = new Point(20, 70),
                AutoSize = true
            };
            this.Controls.Add(lblName);

            var txtName = new TextBox
            {
                Text = inst.DisplayName,
                Location = new Point(150, 68),
                Width = 300
            };
            this.Controls.Add(txtName);

            var lblLoader = new Label
            {
                Text = "Loader Type:",
                ForeColor = Color.White,
                Location = new Point(20, 150),
                AutoSize = true
            };
            this.Controls.Add(lblLoader);

            var comboLoader = new ComboBox
            {
                Location = new Point(150, 148),
                Width = 300,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboLoader.Items.Add("MelonLoader");
            comboLoader.Items.Add("BepInEx");

            if (string.Equals(inst.LoaderType, "BepInEx", StringComparison.OrdinalIgnoreCase))
                comboLoader.SelectedIndex = 1;
            else
                comboLoader.SelectedIndex = 0;

            this.Controls.Add(comboLoader);

            var lblPath = new Label
            {
                Text = "Directory:",
                ForeColor = Color.White,
                Location = new Point(20, 110),
                AutoSize = true
            };
            this.Controls.Add(lblPath);

            var txtPath = new TextBox
            {
                Text = inst.Path,
                Location = new Point(150, 108),
                Width = 260
            };
            this.Controls.Add(txtPath);

            var btnBrowse = new Button
            {
                Text = "...",
                Height = 20,
                Width = 50,
                Location = new Point(420, 106)
            };
            StyleButton(btnBrowse);
            btnBrowse.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            btnBrowse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                        txtPath.Text = dialog.SelectedPath;
                }
            };
            this.Controls.Add(btnBrowse);

            var btnSave = new Button
            {
                Text = "Save",
                Height = 35,
                Width = 150,
                Location = new Point(20, this.ClientSize.Height - 80)
            };
            StyleButton(btnSave);
            btnSave.BackColor = Color.Green;

            btnSave.Click += (s, e) =>
            {
                string newName = txtName.Text.Trim();
                string newPath = txtPath.Text.Trim();

                if (string.IsNullOrWhiteSpace(newName))
                {
                    MessageBox.Show("Name cannot be empty.");
                    return;
                }

                if (!Directory.Exists(newPath))
                {
                    MessageBox.Show("Directory does not exist.");
                    return;
                }

                string oldLoaderType = inst.LoaderType;
                string newLoaderType = comboLoader.SelectedItem.ToString();

                bool loaderChanged = !string.Equals(
                    oldLoaderType,
                    newLoaderType,
                    StringComparison.OrdinalIgnoreCase
                );

                inst.DisplayName = newName;
                inst.Path = newPath;

                currentDirectory = newPath;

                if (loaderChanged)
                {
                    if (IsPatched(inst.Path) == true)
                    {
                        MessageBox.Show("Please unpatch first");
                    }
                    else
                    {
                        MessageBox.Show("Loader type changed, you can now patch");
                        inst.LoaderType = newLoaderType;
                    }
                }

                RefreshInstallationMetadata(inst);

                SaveInstallPaths();
                LoadInstallPaths();
                ShowInstallationManager();
            };
            this.Controls.Add(btnSave);

            var btnCancel = new Button
            {
                Text = "Cancel",
                Height = 35,
                Width = 150,
                Location = new Point(160, this.ClientSize.Height - 80)
            };
            StyleButton(btnCancel);

            btnCancel.Click += (s, e) =>
            {
                ShowInstallationManager();
            };
            this.Controls.Add(btnCancel);
        }

        private void ShowInstallationManager()
        {
            this.Controls.Clear();

            var title = new Label
            {
                Text = "Installations",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };
            this.Controls.Add(title);

            installPanel = new Panel
            {
                Location = new Point(20, 50),
                Size = new Size(this.ClientSize.Width - 40, this.ClientSize.Height - 140),
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(installPanel);

            foreach (var inst in installPaths.ToList())
            {
                RefreshInstallationMetadata(inst);

                Panel row = new Panel
                {
                    Height = 70,
                    Dock = DockStyle.Top,
                    BackColor = Color.FromArgb(50, 50, 50)
                };

                var lblPath = new Label
                {
                    Text = inst.Path,
                    ForeColor = Color.White,
                    Location = new Point(10, 5),
                    AutoSize = true
                };

                string lastPlayedText =
                    inst.LastPlayed == DateTime.MinValue ? "Never" : inst.LastPlayed.ToString("g");

                var lblMeta = new Label
                {
                    Text =
                        $"Name: {inst.DisplayName} | Mods: {inst.ModCount} | Patched: {GetPatchType(inst.Path)} | Last Played: {lastPlayedText}",
                    ForeColor = Color.LightGray,
                    Location = new Point(10, 25),
                    AutoSize = true
                };

                Button btnEdit = new Button
                {
                    Text = "Edit",
                    Width = 150,
                    Height = 35,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                StyleButton(btnEdit);

                Button btnSelect = new Button
                {
                    Text = "Select",
                    Width = 150,
                    Height = 35,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                StyleButton(btnSelect);

                Button btnRemove = new Button
                {
                    Text = "X",
                    Width = 40,
                    Height = 30,
                    BackColor = Color.DarkRed,
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                btnRemove.FlatAppearance.BorderSize = 0;

                Button btnMods = new Button
                {
                    Text = "Mods",
                    Width = 150,
                    Height = 35,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right
                };
                StyleButton(btnMods);

                btnSelect.Click += (s, e) =>
                {
                    currentDirectory = inst.Path;
                    RebuildMainUI();
                };

                btnEdit.Click += (s, e) =>
                {
                    ShowInlineEditor(inst);
                };

                btnRemove.Click += (s, e) =>
                {
                    installPaths.Remove(inst);
                    SaveInstallPaths();
                    ShowInstallationManager();
                };

                btnMods.Click += (s, e) =>
                {
                    string modsPath = null;

                    if (inst.LoaderType == "BepInEx")
                    {
                        modsPath = Path.Combine(inst.Path, "BepInEx", "Plugins");
                    }
                    else
                    {
                        modsPath = Path.Combine(inst.Path, "Mods");
                        if (!Directory.Exists(modsPath))
                            Directory.CreateDirectory(modsPath);
                    }

                    Process.Start("explorer.exe", modsPath);
                };

                row.Controls.Add(lblPath);
                row.Controls.Add(lblMeta);
                row.Controls.Add(btnSelect);
                row.Controls.Add(btnRemove);
                row.Controls.Add(btnEdit);
                row.Controls.Add(btnMods);

                installPanel.Controls.Add(row);
                installPanel.Controls.SetChildIndex(row, 0);

                btnSelect.Width = btnSelect.Width / 2;
                btnEdit.Width = btnEdit.Width / 2;
                btnMods.Width = btnMods.Width / 2;

                btnRemove.Location = new Point(row.Width - btnRemove.Width - 10, 20);
                btnSelect.Location = new Point(btnRemove.Left - btnSelect.Width - 10, 20);
                btnEdit.Location = new Point(btnSelect.Left - btnEdit.Width - 10, 20);
                btnMods.Location = new Point(btnEdit.Left - btnMods.Width - 10, 20);

                row.Resize += (s, e) =>
                {
                    btnRemove.Left = row.Width - btnRemove.Width - 10;
                    btnSelect.Left = btnRemove.Left - btnSelect.Width - 10;
                    btnEdit.Left = btnSelect.Left - btnEdit.Width - 10;
                    btnMods.Left = btnEdit.Left - btnMods.Width - 10;
                };
            }

            Button add = new Button
            {
                Text = "+ Add Installation",
                Width = 200,
                Height = 40,
                Location = new Point(20, this.ClientSize.Height - 80)
            };
            StyleButton(add);
            add.BackColor = Color.Green;

            add.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog())
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        string path = dialog.SelectedPath;
                        if (
                            !installPaths.Any(
                                i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)
                            )
                        )
                        {
                            var info = CreateInstallationInfo(path);
                            installPaths.Add(info);
                            SaveInstallPaths();
                            ShowInstallationManager();
                        }
                    }
                }
            };
            this.Controls.Add(add);

            var store = LoadInstallationStore();
            if (store != null && store.Versions.Count > 0)
            {
                Button download = new Button
                {
                    Text = "Download New",
                    Width = 200,
                    Height = 40,
                    Location = new Point(240, this.ClientSize.Height - 80)
                };
                StyleButton(download);

                download.Click += (s, e) =>
                {
                    ShowCreateInstallationMenu(false);
                };

                this.Controls.Add(download);
            }

            Button back = new Button
            {
                Text = "Go Back",
                Width = 200,
                Height = 40,
                Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 80)
            };
            StyleButton(back);

            back.Click += (s, e) =>
            {
                RebuildMainUI();
            };
            this.Controls.Add(back);
        }

        private void ShowCreateInstallationMenu(bool openedFromMainMenu)
        {
            this.Controls.Clear();
            var store = LoadInstallationStore();

            string selectedVersionZip = null;
            string selectedModpackZip = null;
            string selectedNet6Zip = null;
            string selectedTranslationZip = null;

            Label title = new Label();
            title.Text = "Create New Installation";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 14, FontStyle.Bold);
            title.Location = new Point(20, 15);
            title.AutoSize = true;
            this.Controls.Add(title);

            ComboBox versionBox = new ComboBox();
            versionBox.Location = new Point(20, 70);
            versionBox.Width = 300;
            versionBox.DropDownStyle = ComboBoxStyle.DropDownList;
            versionBox.Items.Add("<Select Version>");
            versionBox.SelectedIndex = 0;
            this.Controls.Add(versionBox);

            ComboBox loaderBox = new ComboBox();
            loaderBox.Location = new Point(20, 120);
            loaderBox.Width = 300;
            loaderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            loaderBox.Items.Add("<Select Loader>");
            loaderBox.Items.Add("MelonLoader");
            loaderBox.Items.Add("BepInEx");
            loaderBox.Items.Add("None");
            loaderBox.SelectedIndex = 0;
            this.Controls.Add(loaderBox);

            ComboBox languageBox = new ComboBox();
            languageBox.Location = new Point(20, 170);
            languageBox.Width = 300;
            languageBox.DropDownStyle = ComboBoxStyle.DropDownList;

            languageBox.Items.Add("中文 (Chinese)");
            languageBox.Items.Add("العربية");
            languageBox.Items.Add("English");
            languageBox.Items.Add("Filipino");
            languageBox.Items.Add("Français");
            languageBox.Items.Add("Deutsch");
            languageBox.Items.Add("Bahasa Indonesia");
            languageBox.Items.Add("Italiano");
            languageBox.Items.Add("日本語");
            languageBox.Items.Add("Basa Jawa");
            languageBox.Items.Add("한국어");
            languageBox.Items.Add("Polski");
            languageBox.Items.Add("Português");
            languageBox.Items.Add("Română");
            languageBox.Items.Add("Русский");
            languageBox.Items.Add("Español");
            languageBox.Items.Add("Türkçe");
            languageBox.Items.Add("Українська");
            languageBox.Items.Add("Tiếng Việt");

            Dictionary<string, string> languageMap = new Dictionary<string, string>();
            languageMap.Add("中文 (Chinese)", "Chinese");
            languageMap.Add("العربية", "Arabic");
            languageMap.Add("English", "English");
            languageMap.Add("Filipino", "Filipino");
            languageMap.Add("Français", "French");
            languageMap.Add("Deutsch", "German");
            languageMap.Add("Bahasa Indonesia", "Indonesian");
            languageMap.Add("Italiano", "Italian");
            languageMap.Add("日本語", "Japanese");
            languageMap.Add("Basa Jawa", "Javanese");
            languageMap.Add("한국어", "Korean");
            languageMap.Add("Polski", "Polish");
            languageMap.Add("Português", "Portuguese");
            languageMap.Add("Română", "Romanian");
            languageMap.Add("Русский", "Russian");
            languageMap.Add("Español", "Spanish");
            languageMap.Add("Türkçe", "Turkish");
            languageMap.Add("Українська", "Ukrainian");
            languageMap.Add("Tiếng Việt", "Vietnamese");

            languageBox.SelectedIndex = 0;
            this.Controls.Add(languageBox);

            Button modpackBtn = new Button();
            modpackBtn.Text = "Select .Modpack (Optional)";
            modpackBtn.Width = 300;
            modpackBtn.Height = 40;
            modpackBtn.Location = new Point(20, 220);
            StyleButton(modpackBtn);

            modpackBtn.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Select a Modpack";
                    dialog.Filter = "Modpacks (*.Modpack)|*.Modpack";

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        selectedModpackZip = dialog.FileName;
                        MessageBox.Show("Modpack selected:\n" + selectedModpackZip);
                    }
                }
            };
            this.Controls.Add(modpackBtn);

            languageBox.Visible = false;
            modpackBtn.Visible = false;
            loaderBox.Visible = false;

            languageBox.SelectedIndexChanged += delegate
            {
                string lang = languageMap[languageBox.SelectedItem.ToString()];

                if (lang == "Chinese")
                    return;

                DialogResult confirm = MessageBox.Show(
                    "To use this language, you must download and install the Translation Mod.\n\n" +
                    "IMPORTANT!! FIND YOUR VERSION IN RELEASES\n" +
                    "LOOK FOR THE FOLLOWING:\n" +
                    "'Manual MelonLoader Installation Walkthrough'\n" +
                    "SKIP STEP 1 AND STEP 2\n" +
                    "SKIP STEPS WITH 'Extract' \n\n" +
                    "Would you like to continue?",

                    "Translation Mod Required",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information
                );

                if (confirm == DialogResult.Cancel)
                {
                    languageBox.SelectedIndex = 0;
                    MessageBox.Show("Defaulting language.");
                    return;
                }

                DialogResult downloaded = MessageBox.Show(
                    "Have you already downloaded the Translation Mod files?\n\n"
                        + "You need BOTH:\n"
                        + " • net6.zip\n"
                        + " • PVZF-Translation.zip",
                    "Translation Mod Download",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (downloaded == DialogResult.No)
                {
                    try
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = "https://github.com/Teyliu/PVZF-Translation/releases",
                                UseShellExecute = true
                            }
                        );
                    }
                    catch
                    {
                        MessageBox.Show("Unable to open the Translation Mod release page.");
                    }
                }

                DialogResult selectFiles = MessageBox.Show(
                    "You will now be asked to select the required files.\n\n"
                        + "First: net6.zip\n"
                        + "Then: PVZF-Translation.zip",
                    "File Selection",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information
                );

                if (selectFiles == DialogResult.Cancel)
                {
                    languageBox.SelectedIndex = 0;
                    MessageBox.Show("Defaulting language.");
                    return;
                }

                while (true)
                {
                    using (OpenFileDialog dialog = new OpenFileDialog())
                    {
                        dialog.Title = "Select net6.zip";
                        dialog.Filter = "ZIP Files (*.zip)|*.zip";

                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            languageBox.SelectedIndex = 0;
                            MessageBox.Show("Defaulting language.");
                            return;
                        }

                        string net6Zip = dialog.FileName;

                        if (IsValidNet6Zip(net6Zip))
                        {
                            selectedNet6Zip = net6Zip;
                            break;
                        }

                        MessageBox.Show(
                            "Invalid net6.zip.\nCould not find Il2CppInterop.Common.dll inside a folder named 'net6'.",
                            "Invalid net6.zip",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                }

                while (true)
                {
                    using (OpenFileDialog dialog = new OpenFileDialog())
                    {
                        dialog.Title = "Select TranslationMod.zip";
                        dialog.Filter = "ZIP Files (*.zip)|*.zip";

                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            languageBox.SelectedIndex = 0;
                            MessageBox.Show("Defaulting language.");
                            return;
                        }

                        string translationZip = dialog.FileName;

                        if (IsValidTranslationModZip(translationZip))
                        {
                            selectedTranslationZip = translationZip;
                            break;
                        }

                        MessageBox.Show(
                            "Invalid TranslationMod.zip.\nCould not find PvZ_Fusion_Translator.dll.",
                            "Invalid TranslationMod.zip",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                }

                MessageBox.Show("Translation Mod files selected successfully.");
            };

            loaderBox.SelectedIndexChanged += delegate
            {
                string loader = loaderBox.SelectedItem.ToString();
                if (loader == "<Select Loader>")
                {
                    languageBox.Visible = false;
                    modpackBtn.Visible = false;
                }
                if (loader == "MelonLoader")
                {
                    languageBox.Enabled = true;
                    languageBox.Visible = true;
                    modpackBtn.Visible = true;
                }
                else if (loader == "BepInEx")
                {
                    languageBox.SelectedIndex = 0;
                    languageBox.Enabled = false;
                    languageBox.Visible = true;
                    modpackBtn.Visible = true;
                }
                else if (loader == "None")
                {
                    languageBox.Visible = false;
                    modpackBtn.Visible = false;
                }
            };

            foreach (var entry in store.Versions)
                versionBox.Items.Add(entry.Version);

            Button installBtn = new Button();
            installBtn.Text = "Install";
            installBtn.Width = 200;
            installBtn.Height = 40;
            installBtn.Location = new Point(20, 280);
            StyleButton(installBtn);
            installBtn.BackColor = Color.Green;
            this.Controls.Add(installBtn);

            versionBox.SelectedIndexChanged += delegate
            {
                if (versionBox.SelectedItem == null || versionBox.SelectedIndex == 0)
                {
                    loaderBox.Visible = false;
                    languageBox.Visible = false;
                    modpackBtn.Visible = false;
                    return;
                }
                loaderBox.Visible = true;
                string selectedVersion = versionBox.SelectedItem.ToString();
                var entry = store.Versions.FirstOrDefault(v => v.Version == selectedVersion);

                if (entry == null)
                    return;

                DialogResult confirm = MessageBox.Show(
                    "Download version " + selectedVersion + " now?",
                    "Download Version",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question
                );

                if (confirm == DialogResult.Cancel)
                {
                    DialogResult local = MessageBox.Show(
                        "Would you like to select an already downloaded " + selectedVersion + ".ZIP instead?",
                        "Select Local ZIP",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (local == DialogResult.No)
                    {
                        return;
                    }
                }
                else
                {
                    try
                    {
                        Process.Start(
                            new ProcessStartInfo
                            {
                                FileName = entry.DownloadUrl,
                                UseShellExecute = true
                            }
                        );
                    }
                    catch
                    {
                        MessageBox.Show("Unable to open the download link.");
                        return;
                    }
                }

                while (true)
                {
                    using (OpenFileDialog dialog = new OpenFileDialog())
                    {
                        dialog.Title = "Select the downloaded version ZIP file";
                        dialog.Filter = "ZIP Files (*.zip)|*.zip";

                        if (dialog.ShowDialog() != DialogResult.OK)
                        {
                            selectedVersionZip = null;
                            return;
                        }

                        string versionZip = dialog.FileName;

                        if (IsValidVersionZip(versionZip))
                        {
                            selectedVersionZip = versionZip;
                            MessageBox.Show("Version ZIP selected:\n" + selectedVersionZip);
                            return;
                        }

                        MessageBox.Show(
                            "Invalid version ZIP.\nCould not find PlantsVsZombiesRH.exe.",
                            "Invalid Version ZIP",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                }
            };

            installBtn.Click += delegate
            {
                string version = versionBox.SelectedItem.ToString();
                string loader = loaderBox.SelectedItem.ToString();
                string language = languageMap[languageBox.SelectedItem.ToString()];
            
                if (version == "<Select Version>")
                {
                    MessageBox.Show("Please select a version to install");
                    return;
                }

                if (loader == "<Select Loader>")
                {
                    MessageBox.Show("Please select a loader to package");
                    return;
                }

                if (string.IsNullOrEmpty(selectedVersionZip))
                {
                    MessageBox.Show("Please select a valid version ZIP before installing.");
                    return;
                }

                CreateNewInstallationFromLocalZip(
                    version,
                    loader,
                    selectedVersionZip,
                    selectedModpackZip,
                    language,
                    selectedNet6Zip,
                    selectedTranslationZip
                );
            };

            Button backBtn = new Button();
            backBtn.Text = "Go Back";
            backBtn.Width = 200;
            backBtn.Height = 40;
            backBtn.Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 80);
            StyleButton(backBtn);

            backBtn.Click += delegate
            {
                if (openedFromMainMenu)
                    RebuildMainUI();
                else
                    ShowInstallationManager();
            };
            this.Controls.Add(backBtn);

            if (openedFromMainMenu)
            {
                Button addExistingBtn = new Button();
                addExistingBtn.Text = "Add Existing";
                addExistingBtn.Width = 200;
                addExistingBtn.Height = 40;
                addExistingBtn.Location = new Point(
                    this.ClientSize.Width - 440,
                    this.ClientSize.Height - 80
                );
                StyleButton(addExistingBtn);

                addExistingBtn.Click += delegate
                {
                    using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                    {
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            string path = dialog.SelectedPath;

                            bool exists = false;
                            foreach (var i in installPaths)
                            {
                                if (string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))
                                {
                                    exists = true;
                                    break;
                                }
                            }

                            if (!exists)
                            {
                                var info = CreateInstallationInfo(path);
                                installPaths.Add(info);
                                SaveInstallPaths();
                            }

                            RebuildMainUI();
                        }
                    }
                };

                this.Controls.Add(addExistingBtn);
            }
        }

        private void RebuildMainUI()
        {
            string selected = currentDirectory;

            this.Controls.Clear();
            InitializeComponent();
            LoadInstallPaths();

            if (
                !string.IsNullOrEmpty(selected)
                && installPaths.Any(
                    i => string.Equals(i.Path, selected, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                currentDirectory = selected;
                lblDirectory.Text = selected;
            }
            else
            {
                SelectDefaultInstallation();
            }

            UpdateBrowseButtonText();
            UpdatePatchButtonState();
        }

        private void btnPatch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentDirectory))
            {
                MessageBox.Show("Select a directory first.");
                return;
            }

            if (IsPatched(currentDirectory))
                UnpatchGame(currentDirectory);
            else
                PatchGame();

            UpdatePatchButtonState();
        }

        private void SafeMove(string source, string destDir)
        {
            try
            {
                if (!File.Exists(source))
                    return;

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                string dest = Path.Combine(destDir, Path.GetFileName(source));

                if (File.Exists(dest))
                    File.Delete(dest);

                File.Move(source, dest);
            }
            catch { }
        }

        private void SafeMoveDirectory(string sourceDir, string destDir)
        {
            try
            {
                if (!Directory.Exists(sourceDir))
                    return;

                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                string dest = Path.Combine(destDir, Path.GetFileName(sourceDir));

                if (Directory.Exists(dest))
                    Directory.Delete(dest, true);

                Directory.Move(sourceDir, dest);
            }
            catch { }
        }

        private void PatchGame()
        {
            var inst = installPaths.FirstOrDefault(
                i => string.Equals(i.Path, currentDirectory, StringComparison.OrdinalIgnoreCase)
            );

            if (inst == null)
            {
                MessageBox.Show("Installation not found.");
                return;
            }

            try
            {
                string disabledMelon = Path.Combine(currentDirectory, "_DISABLED_MELONLOADER");
                string disabledBep = Path.Combine(currentDirectory, "_DISABLED_BEPINEX");

                bool restored = false;

                if (inst.LoaderType == "MelonLoader" && Directory.Exists(disabledMelon))
                {
                    foreach (var dir in Directory.GetDirectories(disabledMelon))
                        SafeMoveDirectory(dir, currentDirectory);

                    foreach (var file in Directory.GetFiles(disabledMelon))
                        SafeMove(file, currentDirectory);

                    Directory.Delete(disabledMelon, true);
                    restored = true;
                }

                if (inst.LoaderType == "BepInEx" && Directory.Exists(disabledBep))
                {
                    foreach (var dir in Directory.GetDirectories(disabledBep))
                        SafeMoveDirectory(dir, currentDirectory);

                    foreach (var file in Directory.GetFiles(disabledBep))
                        SafeMove(file, currentDirectory);

                    Directory.Delete(disabledBep, true);
                    restored = true;
                }

                if (!restored)
                {
                    if (inst.LoaderType == "MelonLoader")
                        ExtractMelonLoader(currentDirectory);
                    else if (inst.LoaderType == "BepInEx")
                        ExtractBepInEx(currentDirectory);
                    else
                    {
                        MessageBox.Show("Unknown loader type.");
                        return;
                    }
                }

                RefreshInstallationMetadata(inst);
                SaveInstallPaths();
                MessageBox.Show("Game patched successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Patch failed: " + ex.Message);
            }
        }

        private void UnpatchGame(string path)
        {
            var inst = installPaths.FirstOrDefault(
                i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)
            );

            if (inst == null)
            {
                MessageBox.Show("Installation not found.");
                return;
            }

            try
            {
                string melonDir = Path.Combine(path, "MelonLoader");
                if (Directory.Exists(melonDir))
                {
                    string disabled = Path.Combine(path, "_DISABLED_MELONLOADER");

                    SafeMoveDirectory(melonDir, disabled);
                    SafeMoveDirectory(Path.Combine(path, "Mods"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "Plugins"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "UserData"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "UserLibs"), disabled);

                    SafeMove(Path.Combine(path, "version.dll"), disabled);
                }

                string bepDir = Path.Combine(path, "BepInEx");
                if (Directory.Exists(bepDir))
                {
                    string disabled = Path.Combine(path, "_DISABLED_BEPINEX");

                    SafeMoveDirectory(bepDir, disabled);

                    SafeMove(Path.Combine(path, ".doorstop_version"), disabled);
                    SafeMove(Path.Combine(path, "changelog.txt"), disabled);
                    SafeMove(Path.Combine(path, "doorstop_config.ini"), disabled);
                    SafeMove(Path.Combine(path, "winhttp.dll"), disabled);
                }

                RefreshInstallationMetadata(inst);
                SaveInstallPaths();

                MessageBox.Show("Game unpatched successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unpatch failed: " + ex.Message);
            }
        }

        private void btnLaunch_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(currentDirectory))
            {
                MessageBox.Show("Select a directory first.");
                return;
            }

            if (!Directory.Exists(currentDirectory))
            {
                MessageBox.Show("Selected directory does not exist:\n" + currentDirectory);
                return;
            }

            try
            {
                string exe = Directory
                    .GetFiles(currentDirectory, "*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(
                        f =>
                        {
                            string lower = Path.GetFileName(f).ToLowerInvariant();
                            return !lower.Contains("unity")
                                && !lower.Contains("crash")
                                && !lower.Contains("mono")
                                && !lower.Contains("install");
                        }
                    );

                if (exe == null)
                {
                    MessageBox.Show("No valid game executable found in:\n" + currentDirectory);
                    return;
                }

                Process.Start(exe);
                Application.Exit();

                var inst = installPaths.FirstOrDefault(
                    i => string.Equals(i.Path, currentDirectory, StringComparison.OrdinalIgnoreCase)
                );

                if (inst != null)
                {
                    inst.LastPlayed = DateTime.Now;
                    SaveInstallPaths();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to launch: " + ex.Message);
            }
        }

        private void btnQuit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}