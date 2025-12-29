using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PVZF_Launcher.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace PVZF_Launcher
{
    public class Launcher : Form
    {
        private ButtonCanvas canvas;

        private Label lblVersion;
        private Label lblDirectory;

        private Panel installPanel;
        private Panel packPanel;

        private string currentDirectory = "";

        private string installListFile;

        public class LauncherOptions
        {
            public string Language { get; set; } = "English";
            public bool AutoUpdate { get; set; } = true;
        }

        private string GetOptionsPath()
        {
            string resDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PVZFL_Resources");
            return Path.Combine(resDir, "Options.json");
        }

        private LauncherOptions LoadOptions()
        {
            string path = GetOptionsPath();

            if (!File.Exists(path))
                return new LauncherOptions();

            try
            {
                string json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<LauncherOptions>(json)
                    ?? new LauncherOptions();
            }
            catch
            {
                return new LauncherOptions();
            }
        }

        private void SaveOptions(LauncherOptions opts)
        {
            string path = GetOptionsPath();
            string json = JsonConvert.SerializeObject(opts, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public class ModPackInfo
        {
            public string Name { get; set; }
            public string Creator { get; set; }
            public string LoaderType { get; set; }
            public string SourceFile { get; set; }
            public List<string> InstalledFiles { get; set; } = new List<string>();
        }

        private List<InstallationInfo> installPaths = new List<InstallationInfo>();

        public class InstallationInfo
        {
            public string Path { get; set; }
            public string ExeName { get; set; }
            public int ModCount { get; set; }
            public bool IsPatched { get; set; }
            public DateTime LastPlayed { get; set; } = DateTime.MinValue;
            public string LoaderType { get; set; } = "MelonLoader";
            public List<ModPackInfo> InstalledModpacks { get; set; } = new List<ModPackInfo>();

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

        public void SaveInstallationInfo(InstallationInfo inst)
        {
            string file = System.IO.Path.Combine(inst.Path, "InstallationInfo.json");
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(inst, Formatting.Indented);
            File.WriteAllText(file, json);
        }

        public InstallationInfo LoadInstallationInfo(string path)
        {
            string file = System.IO.Path.Combine(path, "InstallationInfo.json");
            if (!File.Exists(file))
                return null;

            string json = File.ReadAllText(file);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<InstallationInfo>(json);
        }

        public ModPackInfo ValidateModpack(string modpackPath, string loaderType)
        {
            if (!File.Exists(modpackPath))
                throw new Exception("Modpack file does not exist.");

            using (ZipArchive zip = ZipFile.OpenRead(modpackPath))
            {
                var modpackJsonEntry = zip.GetEntry("ModPack.json");
                if (modpackJsonEntry == null)
                    throw new Exception("ModPack.json is missing from the modpack.");

                bool hasGameDirectory = zip.Entries.Any(
                    e => e.FullName.StartsWith("GameDirectory/", StringComparison.OrdinalIgnoreCase)
                );

                if (!hasGameDirectory)
                    throw new Exception("GameDirectory folder is missing from the modpack.");

                bool hasMelonMods = zip.Entries.Any(
                    e =>
                        e.FullName.StartsWith(
                            "GameDirectory/Mods/",
                            StringComparison.OrdinalIgnoreCase
                        )
                );

                bool hasBepInEx = zip.Entries.Any(
                    e =>
                        e.FullName.StartsWith(
                            "GameDirectory/BepInEx/",
                            StringComparison.OrdinalIgnoreCase
                        )
                );

                string detectedLoader = null;

                if (hasMelonMods)
                    detectedLoader = "MelonLoader";

                if (hasBepInEx)
                    detectedLoader = "BepInEx";

                if (detectedLoader == null)
                    throw new Exception(
                        "Modpack must contain either GameDirectory/Mods/ or GameDirectory/BepInEx/."
                    );

                if (!string.Equals(detectedLoader, loaderType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"This modpack is for {detectedLoader}, but this installation uses {loaderType}."
                    );
                }

                string jsonText;
                using (var reader = new StreamReader(modpackJsonEntry.Open()))
                    jsonText = reader.ReadToEnd();

                JObject json = JObject.Parse(jsonText);

                string name = json["name"]?.ToString();
                string creator = json["creator"]?.ToString();

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(creator))
                    throw new Exception("ModPack.json must contain 'name' and 'creator'.");

                return new ModPackInfo
                {
                    Name = name,
                    Creator = creator,
                    LoaderType = loaderType,
                    SourceFile = modpackPath
                };
            }
        }

        public List<string> GetModpackFileList(string modpackPath)
        {
            using (ZipArchive zip = ZipFile.OpenRead(modpackPath))
            {
                return zip.Entries
                    .Where(e => !string.IsNullOrEmpty(e.Name))
                    .Select(
                        e =>
                        {
                            string path = e.FullName;

                            if (path.Equals("ModPack.json", StringComparison.OrdinalIgnoreCase))
                                return null;

                            if (
                                path.StartsWith(
                                    "GameDirectory/",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                                path = path.Substring("GameDirectory/".Length);

                            return path;
                        }
                    )
                    .Where(p => p != null)
                    .ToList();
            }
        }

        public List<string> GetConflicts(InstallationInfo install, ModPackInfo newPack)
        {
            var conflicts = new List<string>();

            foreach (var existing in install.InstalledModpacks)
            {
                var overlap = existing.InstalledFiles.Intersect(
                    newPack.InstalledFiles,
                    StringComparer.OrdinalIgnoreCase
                );

                conflicts.AddRange(overlap);
            }

            return conflicts.Distinct().ToList();
        }

        public void InstallModpack(InstallationInfo inst, ModPackInfo pack)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "modpack_extract_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            ZipFile.ExtractToDirectory(pack.SourceFile, tempDir);

            foreach (var file in pack.InstalledFiles)
            {
                string src = Path.Combine(tempDir, "GameDirectory", file);
                string dst = Path.Combine(inst.Path, file);

                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
            }

            inst.InstalledModpacks.Add(pack);
            inst.ModCount = inst.InstalledModpacks.Count;

            SaveInstallationInfo(inst);

            Directory.Delete(tempDir, true);
        }

        public void UninstallModpack(InstallationInfo inst, ModPackInfo pack)
        {
            string baseDir = inst.Path;

            if (inst.LoaderType == "MelonLoader")
            {
                string disabled = Path.Combine(inst.Path, "_DISABLED_MELONLOADER");
                if (Directory.Exists(disabled))
                    baseDir = disabled;
            }
            else if (inst.LoaderType == "BepInEx")
            {
                string disabled = Path.Combine(inst.Path, "_DISABLED_BEPINEX");
                if (Directory.Exists(disabled))
                    baseDir = disabled;
            }

            var protectedFiles = inst.InstalledModpacks
                .Where(p => p != pack)
                .SelectMany(p => p.InstalledFiles)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in pack.InstalledFiles)
            {
                if (protectedFiles.Contains(file))
                    continue;

                string full = Path.Combine(baseDir, file);
                if (File.Exists(full))
                    File.Delete(full);
            }

            inst.InstalledModpacks.Remove(pack);
            inst.ModCount = inst.InstalledModpacks.Count;

            SaveInstallationInfo(inst);
        }
        private void AddModpackToInstallation(InstallationInfo inst)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Select a Modpack";
                dialog.Filter = "Modpacks (*.Modpack)|*.Modpack";

                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                string modpackZip = dialog.FileName;

                try
                {
                    ModPackInfo pack = ValidateModpack(modpackZip, inst.LoaderType);

                    pack.InstalledFiles = GetModpackFileList(modpackZip);

                    var conflicts = GetConflicts(inst, pack);
                    if (conflicts.Count > 0)
                    {
                        string preview = string.Join("\n", conflicts.Take(20));
                        MessageBox.Show(
                            $"Warning: This modpack overwrites {conflicts.Count} files:\n\n{preview}",
                            "Modpack Conflict",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }

                    InstallModpack(inst, pack);

                    SaveInstallationInfo(inst);

                    MessageBox.Show($"Modpack '{pack.Name}' installed successfully!");

                    ShowModpackManager(inst);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Failed to install modpack:\n" + ex.Message);
                }
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
        private static void UnpackButtonImage(string baseDir, string fileName, byte[] resourceBytes)
        {
            string resDir = EnsureResourceFolder(baseDir);
            string imagePath = Path.Combine(resDir, fileName);

            if (!File.Exists(imagePath))
            {
                using (
                    var fs = new FileStream(
                        imagePath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None
                    )
                )
                {
                    fs.Write(resourceBytes, 0, resourceBytes.Length);
                    fs.Flush(true);
                }
            }
        }

        private Image LoadButtonFromFile(string fileName)
        {
            string path = Path.Combine(currentDirectory, "PVZFL_Resources", fileName);

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (File.Exists(path))
                        return Image.FromFile(path);
                    using (
                        var fs = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.ReadWrite
                        )
                    )
                    {
                        return Image.FromStream(fs);
                    }
                }
                catch
                {
                    Thread.Sleep(50);
                }
            }

            return null;
        }

        private Image BytesToImage(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
                return Image.FromStream(ms);
        }

        private void LoadIcon()
        {
            this.Icon = Properties.Resources.app;
        }

        public class ButtonCanvas : Control
        {
            private List<LauncherButton> buttons = new List<LauncherButton>();

            public LauncherButton GetButton(string id)
            {
                return buttons.FirstOrDefault(b => b.Id == id);
            }
            public ButtonCanvas()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                        | ControlStyles.UserPaint
                        | ControlStyles.OptimizedDoubleBuffer
                        | ControlStyles.SupportsTransparentBackColor,
                    true
                );

                this.BackColor = Color.Transparent;
            }

            public void AddButton(LauncherButton btn)
            {
                buttons.Add(btn);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                foreach (var btn in buttons)
                    btn.Draw(e.Graphics);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                foreach (var btn in buttons)
                    btn.UpdateHover(e.Location);

                Invalidate();
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                foreach (var btn in buttons)
                    btn.CheckClick(e.Location);
            }
        }

        public class LauncherButton
        {
            public string Id;
            public Image Image;
            public Rectangle Bounds;
            public bool Hover;
            public Action OnClick;

            public void Draw(Graphics g)
            {
                if (Image == null)
                    return;

                g.DrawImage(Image, Bounds);

                if (Hover)
                {
                    using (Bitmap bmp = new Bitmap(Image))
                    {
                        for (int y = 0; y < bmp.Height; y++)
                        {
                            for (int x = 0; x < bmp.Width; x++)
                            {
                                Color px = bmp.GetPixel(x, y);

                                if (px.A > 10)
                                {
                                    using (
                                        var pen = new SolidBrush(Color.FromArgb(60, Color.White))
                                    )
                                    {
                                        g.FillRectangle(pen, Bounds.X + x, Bounds.Y + y, 1, 1);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            public void UpdateHover(Point p)
            {
                Hover = Bounds.Contains(p);
            }

            public void CheckClick(Point p)
            {
                if (Bounds.Contains(p))
                    OnClick?.Invoke();
            }
        }
        private void CreateCanvas()
        {
            canvas = new ButtonCanvas();
            canvas.Dock = DockStyle.Fill;
            this.Controls.Add(canvas);
        }

        private void LoadButtons()
        {
            UnpackButtonImage(currentDirectory, "Launch.png", Properties.Resources.Launch);
            UnpackButtonImage(currentDirectory, "Unpatch.png", Properties.Resources.Unpatch);
            UnpackButtonImage(currentDirectory, "Patch.png", Properties.Resources.Patch);
            UnpackButtonImage(currentDirectory, "ModPacks.png", Properties.Resources.ModPacks);
            UnpackButtonImage(
                currentDirectory,
                "ManageInstalls.png",
                Properties.Resources.ManageInstalls
            );
            UnpackButtonImage(
                currentDirectory,
                "CreateInstall.png",
                Properties.Resources.CreateInstall
            );
            UnpackButtonImage(
                currentDirectory,
                "CommunityServer.png",
                Properties.Resources.Discord
            );
            UnpackButtonImage(
                currentDirectory,
                "CreditPlant.png",
                Properties.Resources.CreditPlant
            );
            UnpackButtonImage(
                currentDirectory,
                "OptionPlant.png",
                Properties.Resources.OptionPlant
            );
            UnpackButtonImage(currentDirectory, "QuitPlant.png", Properties.Resources.QuitPlant);

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Launch",
                    Image = LoadButtonFromFile("Launch.png"),
                    Bounds = new Rectangle(450, 80, 176, 52),
                    OnClick = () => btnLaunch_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Patch",
                    Image = LoadButtonFromFile("Patch.png"),
                    Bounds = new Rectangle(454, 135, 175, 49),
                    OnClick = () => btnPatch_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Packs",
                    Image = LoadButtonFromFile("ModPacks.png"),
                    Bounds = new Rectangle(462, 181, 164, 45),
                    OnClick = () => btnModPacks_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Browse",
                    Image = LoadButtonFromFile("CreateInstall.png"),
                    Bounds = new Rectangle(466, 223, 157, 51),
                    OnClick = () => btnBrowse_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Community",
                    Image = LoadButtonFromFile("CommunityServer.png"),
                    Bounds = new Rectangle(0, 0, 222, 110),
                    OnClick = () => btnCommunity_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Options",
                    Image = LoadButtonFromFile("OptionPlant.png"),
                    Bounds = new Rectangle(550, 269, 59, 59),
                    OnClick = () => btnOption_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Credits",
                    Image = LoadButtonFromFile("CreditPlant.png"),
                    Bounds = new Rectangle(604, 271, 39, 66),
                    OnClick = () => btnCredit_Click(null, null)
                }
            );

            canvas.AddButton(
                new LauncherButton
                {
                    Id = "Quit",
                    Image = LoadButtonFromFile("QuitPlant.png"),
                    Bounds = new Rectangle(638, 266, 44, 63),
                    OnClick = () => btnQuit_Click(null, null)
                }
            );
        }

        public Launcher()
        {
            InitializeComponent();
            CreateCanvas();
            LoadButtons();
            LoadBackgroundImage();
            LoadIcon();

            string resDir = EnsureResourceFolder(AppDomain.CurrentDomain.BaseDirectory);
            installListFile = Path.Combine(resDir, "installations.json");

            LoadInstallPaths();
            AutoDetectAndRegisterInstallation();
            SelectDefaultInstallation();
            UpdateBrowseButtonImage();
            UpdatePatchButtonImage();
        }

        private void InitializeComponent()
        {
            this.lblVersion = new Label();
            this.lblDirectory = new Label();
            this.DoubleBuffered = true;

            this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            this.SetStyle(ControlStyles.UserPaint, true);

            this.SuspendLayout();

            this.lblVersion.AutoSize = true;
            this.lblVersion.ForeColor = Color.White;
            this.lblVersion.BackColor = Color.FromArgb(60, 60, 60);
            this.lblVersion.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            this.lblVersion.Text = $"v{Application.ProductVersion}";
            this.lblVersion.Location = new Point(5, 335);
            this.lblVersion.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            this.Controls.Add(this.lblVersion);

            this.lblDirectory.AutoSize = true;
            this.lblDirectory.ForeColor = Color.White;
            this.lblDirectory.BackColor = Color.FromArgb(60, 60, 60);
            this.lblDirectory.Location = new Point(0, 0);
            this.lblDirectory.Name = "lblDirectory";
            this.lblDirectory.Size = new Size(107, 13);
            this.lblDirectory.TabIndex = 0;
            this.lblDirectory.Text = "No directory selected";
            this.Controls.Add(this.lblDirectory);

            this.ClientSize = new Size(750, 350);
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Text = "PvZ RH Mod Installer";
            this.DoubleBuffered = true;

            this.ResumeLayout(false);
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

        bool RequiresNet6(string versionString)
        {
            Version v = new Version(versionString);
            return v >= new Version("3.1.1");
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
                    ModPackInfo pack = ValidateModpack(modpackZipPath, loader);
                    pack.InstalledFiles = GetModpackFileList(modpackZipPath);
                    InstallationInfo inst =
                        LoadInstallationInfo(installPath) ?? CreateInstallationInfo(installPath);
                    var conflicts = GetConflicts(inst, pack);
                    if (conflicts.Count > 0)
                    {
                        string preview = string.Join("\n", conflicts.Take(20));
                        MessageBox.Show(
                            $"Warning: This modpack overwrites {conflicts.Count} files:\n\n{preview}",
                            "Modpack Conflict",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning
                        );
                    }
                    InstallModpack(inst, pack);
                    SaveInstallationInfo(inst);
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
                        if (RequiresNet6(version))
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
                        if (RequiresNet6(version))
                        {
                            InstallNet6(selectedNet6Zip, installPath);
                        }
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
                {
                    RefreshInstallationMetadata(inst);
                    var info = LoadInstallationInfo(inst.Path);
                    if (info != null)
                    {
                        inst.InstalledModpacks = info.InstalledModpacks ?? new List<ModPackInfo>();
                        inst.ModCount = info.ModCount;
                        inst.LoaderType = info.LoaderType;
                        inst.LastPlayed = info.LastPlayed;
                    }
                }
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
                if (Directory.Exists(Path.Combine(installPath, "MelonLoader")))
                    return true;
                if (File.Exists(Path.Combine(installPath, "version.dll")))
                    return true;

                if (Directory.Exists(Path.Combine(installPath, "BepInEx")))
                    return true;
                if (File.Exists(Path.Combine(installPath, "winhttp.dll")))
                    return true;
                if (File.Exists(Path.Combine(installPath, "doorstop_config.ini")))
                    return true;

                return false;
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
                string melon = Path.Combine(installPath, "MelonLoader");
                string bepin = Path.Combine(installPath, "BepInEx");

                if (Directory.Exists(melon))
                    return "Melon";

                if (Directory.Exists(bepin))
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
            public string Net6DownloadUrl { get; set; }
            public string TranslationModDownloadUrl { get; set; }
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

        private void UpdatePatchButtonImage()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdatePatchButtonImage));
                return;
            }

            var btn = canvas.GetButton("Patch");
            if (btn == null)
                return;

            Image old = btn.Image;

            if (string.IsNullOrEmpty(currentDirectory))
            {
                btn.Image = LoadButtonFromFile("Patch.png");
                old?.Dispose();
                canvas.Invalidate();
                return;
            }

            bool patched = IsPatched(currentDirectory);
            btn.Image = patched
                ? LoadButtonFromFile("Unpatch.png")
                : LoadButtonFromFile("Patch.png");

            old?.Dispose();
            canvas.Invalidate();
        }

        private void UpdateBrowseButtonImage()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(UpdateBrowseButtonImage));
                return;
            }

            var btn = canvas.GetButton("Browse");
            if (btn == null)
                return;

            Image old = btn.Image;

            btn.Image =
                installPaths.Count == 0
                    ? LoadButtonFromFile("CreateInstall.png")
                    : LoadButtonFromFile("ManageInstalls.png");

            old?.Dispose();
            canvas.Invalidate();
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
            this.SuspendLayout();
            this.Controls.Clear();

            this.BackgroundImage = BytesToImage(Properties.Resources.Menu);
            this.BackgroundImageLayout = ImageLayout.Stretch;

            var title = new Label
            {
                Text = "Edit Installation",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(20, 15),
                AutoSize = true
            };
            this.Controls.Add(title);

            var lblName = new Label
            {
                Text = "Display Name:",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
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
                BackColor = Color.FromArgb(60, 60, 60),
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
                BackColor = Color.FromArgb(60, 60, 60),
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
            this.ResumeLayout(false);

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
                SaveInstallationInfo(inst);
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
            this.SuspendLayout();
            this.Controls.Clear();

            this.BackgroundImage = BytesToImage(Properties.Resources.Menu);
            this.BackgroundImageLayout = ImageLayout.Stretch;

            var title = new Label
            {
                Text = "Installations",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
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
            this.ResumeLayout(false);

            back.Click += (s, e) =>
            {
                RebuildMainUI();
            };
            this.Controls.Add(back);
        }

        private async Task<string> DownloadWithSplashAsync(string url, string fileName)
        {
            string output = Path.Combine(Path.GetTempPath(), fileName);

            using (var client = new WebClient())
            {
                Splash splash = new Splash("Starting download...");
                splash.Show();

                client.DownloadProgressChanged += (s, e) =>
                {
                    splash.UpdateMessage(
                        $"Do not close\n May still be downloading at 0% \n Downloading {fileName}\n{e.ProgressPercentage}%"
                    );
                };

                client.DownloadFileCompleted += (s, e) =>
                {
                    splash.UpdateMessage("Download complete!");
                    Task.Delay(500).Wait();
                    splash.Close();
                };

                await client.DownloadFileTaskAsync(new Uri(url), output);
            }

            return output;
        }

        private string SelectValidVersionZip()
        {
            while (true)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "ZIP Files (*.zip)|*.zip";
                    dialog.Title = "Select Version ZIP";

                    if (dialog.ShowDialog() != DialogResult.OK)
                        return null;

                    if (IsValidVersionZip(dialog.FileName))
                        return dialog.FileName;

                    MessageBox.Show("Invalid version ZIP.\nCould not find PlantsVsZombiesRH.exe.");
                }
            }
        }

        private string SelectValidNet6Zip()
        {
            while (true)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "ZIP Files (*.zip)|*.zip";
                    dialog.Title = "Select net6.zip";

                    if (dialog.ShowDialog() != DialogResult.OK)
                        return null;

                    if (IsValidNet6Zip(dialog.FileName))
                        return dialog.FileName;

                    MessageBox.Show(
                        "Invalid net6.zip.\nCould not find Il2CppInterop.Common.dll inside a folder named 'net6'."
                    );
                }
            }
        }
        private string SelectValidTranslationZip()
        {
            while (true)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = "ZIP Files (*.zip)|*.zip";
                    dialog.Title = "Select TranslationMod.zip";

                    if (dialog.ShowDialog() != DialogResult.OK)
                        return null;

                    if (IsValidTranslationModZip(dialog.FileName))
                        return dialog.FileName;

                    MessageBox.Show(
                        "Invalid TranslationMod.zip.\nCould not find PvZ_Fusion_Translator.dll."
                    );
                }
            }
        }

        private async void ShowCreateInstallationMenu(bool openedFromMainMenu)
        {
            this.SuspendLayout();
            this.Controls.Clear();

            this.BackgroundImage = BytesToImage(Properties.Resources.Menu);
            this.BackgroundImageLayout = ImageLayout.Stretch;

            var store = LoadInstallationStore();

            string selectedVersionZip = null;
            string selectedModpackZip = null;
            string selectedNet6Zip = null;
            string selectedTranslationZip = null;

            Label title = new Label();
            title.Text = "Create New Installation";
            title.ForeColor = Color.White;
            title.BackColor = Color.FromArgb(60, 60, 60);
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

            foreach (var entry in store.Versions)
                versionBox.Items.Add(entry.Version);

            ComboBox loaderBox = new ComboBox();
            loaderBox.Location = new Point(20, 120);
            loaderBox.Width = 300;
            loaderBox.DropDownStyle = ComboBoxStyle.DropDownList;
            loaderBox.Items.Add("<Select Loader>");
            loaderBox.Items.Add("MelonLoader");
            loaderBox.Items.Add("BepInEx");
            loaderBox.Items.Add("None");
            loaderBox.SelectedIndex = 0;
            loaderBox.Visible = false;
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

            Dictionary<string, string> languageMap = new Dictionary<string, string>()
            {
                { "中文 (Chinese)", "Chinese" },
                { "العربية", "Arabic" },
                { "English", "English" },
                { "Filipino", "Filipino" },
                { "Français", "French" },
                { "Deutsch", "German" },
                { "Bahasa Indonesia", "Indonesian" },
                { "Italiano", "Italian" },
                { "日本語", "Japanese" },
                { "Basa Jawa", "Javanese" },
                { "한국어", "Korean" },
                { "Polski", "Polish" },
                { "Português", "Portuguese" },
                { "Română", "Romanian" },
                { "Русский", "Russian" },
                { "Español", "Spanish" },
                { "Türkçe", "Turkish" },
                { "Українська", "Ukrainian" },
                { "Tiếng Việt", "Vietnamese" }
            };

            languageBox.SelectedIndex = 0;
            languageBox.Visible = false;
            this.Controls.Add(languageBox);

            Button modpackBtn = new Button();
            modpackBtn.Text = "Select .Modpack (Optional)";
            modpackBtn.Width = 300;
            modpackBtn.Height = 40;
            modpackBtn.Location = new Point(20, 220);
            StyleButton(modpackBtn);
            modpackBtn.Visible = false;

            modpackBtn.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Select a Modpack";
                    dialog.Filter = "Modpacks (*.Modpack)|*.Modpack";

                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        selectedModpackZip = dialog.FileName;

                        try
                        {
                            string loaderType = loaderBox.SelectedItem?.ToString();
                            if (loaderType == null || loaderType == "None")
                            {
                                MessageBox.Show("Please select a loader first.");
                                return;
                            }

                            ModPackInfo info = ValidateModpack(selectedModpackZip, loaderType);

                            MessageBox.Show(
                                $"Modpack Valid!\n\n"
                                    + $"Name: {info.Name}\n"
                                    + $"Creator: {info.Creator}\n"
                                    + $"Loader: {info.LoaderType}\n"
                                    + $"File: {info.SourceFile}"
                            );
                        }
                        catch (Exception ex)
                        {
                            selectedModpackZip = null;
                            MessageBox.Show("Invalid Modpack:\n" + ex.Message);
                        }
                    }
                }
            };

            this.Controls.Add(modpackBtn);

            Button installBtn = new Button();
            installBtn.Text = "Install";
            installBtn.Width = 200;
            installBtn.Height = 40;
            installBtn.Location = new Point(20, 280);
            StyleButton(installBtn);
            installBtn.BackColor = Color.Green;
            this.Controls.Add(installBtn);

            Button backBtn = new Button();
            backBtn.Text = "Go Back";
            backBtn.Width = 200;
            backBtn.Height = 40;
            backBtn.Location = new Point(310, 280);
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
                addExistingBtn.Location = new Point(530, 280);
                StyleButton(addExistingBtn);

                addExistingBtn.Click += delegate
                {
                    using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                    {
                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            string path = dialog.SelectedPath;

                            bool exists = installPaths.Any(
                                i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)
                            );

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

            versionBox.SelectedIndexChanged += async delegate
            {
                selectedTranslationZip = null;
                selectedNet6Zip = null;

                if (versionBox.SelectedIndex == 0)
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

                DialogResult haveZip = MessageBox.Show(
                    "Do you already have version" + selectedVersion + ".zip Downloaded?",
                    selectedVersion + ".ZIP",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (haveZip == DialogResult.Yes)
                {
                    selectedVersionZip = SelectValidVersionZip();
                    return;
                }

                selectedVersionZip = await DownloadWithSplashAsync(
                    entry.DownloadUrl,
                    selectedVersion + ".zip"
                );

                if (!IsValidVersionZip(selectedVersionZip))
                {
                    MessageBox.Show("Downloaded version is invalid.");
                    selectedVersionZip = null;
                    return;
                }

                MessageBox.Show("Version downloaded successfully.");
            };

            loaderBox.SelectedIndexChanged += delegate
            {
                string loader = loaderBox.SelectedItem.ToString();

                if (loader == "<Select Loader>")
                {
                    languageBox.Visible = false;
                    modpackBtn.Visible = false;
                    return;
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

            languageBox.SelectedIndexChanged += async delegate
            {
                string lang = languageMap[languageBox.SelectedItem.ToString()];
                if (lang == "Chinese")
                    return;
                if (versionBox.SelectedIndex == 0)
                    return;

                string selectedVersion = versionBox.SelectedItem.ToString();
                var entry = store.Versions.First(v => v.Version == selectedVersion);

                bool needsNet6 = RequiresNet6(selectedVersion);

                DialogResult confirm = MessageBox.Show(
                    "To use this language, you must have Translation Mod.\n\n"
                        + "Would you like to install the Translation Mod",
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

                DialogResult haveFiles = MessageBox.Show(
                    needsNet6
                      ? "Do you already have BOTH net6.zip and TranslationMod.zip?"
                      : "Do you already have TranslationMod.zip?",
                    "Translation Mod Files",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                if (haveFiles == DialogResult.Yes)
                {
                    if (needsNet6)
                    {
                        selectedNet6Zip = SelectValidNet6Zip();
                        if (selectedNet6Zip == null)
                        {
                            languageBox.SelectedIndex = 0;
                            return;
                        }
                    }

                    selectedTranslationZip = SelectValidTranslationZip();
                    if (selectedTranslationZip == null)
                    {
                        languageBox.SelectedIndex = 0;
                        return;
                    }

                    MessageBox.Show("Translation Mod files selected successfully.");
                }
                else
                {
                    if (needsNet6)
                    {
                        selectedNet6Zip = await DownloadWithSplashAsync(
                            entry.Net6DownloadUrl,
                            "net6.zip"
                        );

                        if (!IsValidNet6Zip(selectedNet6Zip))
                        {
                            MessageBox.Show("Downloaded net6.zip is invalid.");
                            languageBox.SelectedIndex = 0;
                            return;
                        }
                    }

                    selectedTranslationZip = await DownloadWithSplashAsync(
                        entry.TranslationModDownloadUrl,
                        "TranslationMod.zip"
                    );

                    if (!IsValidTranslationModZip(selectedTranslationZip))
                    {
                        MessageBox.Show("Downloaded TranslationMod.zip is invalid.");
                        languageBox.SelectedIndex = 0;
                        return;
                    }

                    MessageBox.Show("Translation Mod files downloaded successfully.");
                }
            };

            installBtn.Click += async delegate
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
                    MessageBox.Show(
                        "Please select or download a valid version ZIP before installing."
                    );
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

            this.ResumeLayout(false);
        }

        private void RebuildMainUI()
        {
            string selected = currentDirectory;

            this.SuspendLayout();
            this.Controls.Clear();

            InitializeComponent();
            LoadBackgroundImage();
            CreateCanvas();
            LoadButtons();

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

            UpdateBrowseButtonImage();
            UpdatePatchButtonImage();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void ShowModpackManager(InstallationInfo inst)
        {
            this.Controls.Clear();
            this.BackgroundImage = BytesToImage(Properties.Resources.Menu);
            this.BackgroundImageLayout = ImageLayout.Stretch;

            Label title = new Label
            {
                Text = $"Modpacks — {inst.DisplayName}",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(20, 20),
                AutoSize = true
            };
            this.Controls.Add(title);

            packPanel = new Panel
            {
                Location = new Point(20, 50),
                Size = new Size(this.ClientSize.Width - 40, this.ClientSize.Height - 140),
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(packPanel);

            string disabledMelon = Path.Combine(inst.Path, "_DISABLED_MELONLOADER");
            string disabledBep = Path.Combine(inst.Path, "_DISABLED_BEPINEX");

            foreach (var pack in inst.InstalledModpacks.ToList())
            {
                bool isDisabled =
                    (pack.LoaderType == "MelonLoader" && Directory.Exists(disabledMelon))
                    || (pack.LoaderType == "BepInEx" && Directory.Exists(disabledBep));

                Panel row = new Panel
                {
                    Height = 60,
                    Dock = DockStyle.Top,
                    BackColor = Color.FromArgb(50, 50, 50)
                };

                string statusText = isDisabled ? " (DISABLED)" : "";
                string loaderText = pack.LoaderType;

                Label lbl = new Label
                {
                    Text =
                        $"[{loaderText}] {pack.Name} by {pack.Creator} — {pack.InstalledFiles.Count} files{statusText}",
                    ForeColor = isDisabled ? Color.Gray : Color.White,
                    Location = new Point(10, 10),
                    AutoSize = true
                };

                Button uninstall = new Button { Text = "Uninstall", Width = 120, Height = 35 };
                StyleButton(uninstall);

                if (isDisabled)
                {
                    uninstall.Enabled = false;
                    uninstall.BackColor = Color.FromArgb(80, 80, 80);
                }

                uninstall.Click += (s, e) =>
                {
                    UninstallModpack(inst, pack);
                    SaveInstallationInfo(inst);
                    ShowModpackManager(inst);
                };

                row.Controls.Add(lbl);
                row.Controls.Add(uninstall);

                uninstall.Location = new Point(row.Width - uninstall.Width - 10, 12);
                row.Resize += (s, e) =>
                {
                    uninstall.Left = row.Width - uninstall.Width - 10;
                };

                packPanel.Controls.Add(row);
                packPanel.Controls.SetChildIndex(row, 0);
            }

            Button add = new Button
            {
                Text = "+ Add ModPack",
                Width = 200,
                Height = 40,
                Location = new Point(20, this.ClientSize.Height - 80)
            };
            StyleButton(add);
            add.BackColor = Color.Green;

            add.Click += (s, e) =>
            {
                AddModpackToInstallation(inst);
            };

            this.Controls.Add(add);

            Button back = new Button
            {
                Text = "Back",
                Width = 200,
                Height = 40,
                Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 80)
            };
            StyleButton(back);

            back.Click += (s, e) => RebuildMainUI();

            this.Controls.Add(back);
        }

        private void ShowCreditMenu()
        {
            this.SuspendLayout();
            this.Controls.Clear();

            this.BackgroundImage = BytesToImage(Properties.Resources.Credits);
            this.BackgroundImageLayout = ImageLayout.Stretch;

            var exitBtn = new PictureBox
            {
                Image = BytesToImage(Properties.Resources.Back),
                SizeMode = PictureBoxSizeMode.AutoSize,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Location = new Point(639, 296)
            };

            exitBtn.Click += (s, e) => RebuildMainUI();

            this.Controls.Add(exitBtn);

            this.ResumeLayout(false);
        }
        private void ShowOptionsMenu()
        {
            this.SuspendLayout();
            this.Controls.Clear();

            var opts = LoadOptions();

            Panel overlay = new Panel
            {
                BackgroundImage = BytesToImage(Properties.Resources.MenuOverlay),
                BackgroundImageLayout = ImageLayout.Stretch,
                Width = 280,
                Height = 328,
                BackColor = Color.Transparent,
                Location = new Point(426, 9)
            };

            var lblLang = new Label
            {
                Text = "Language:",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Location = new Point(486, 100),
                AutoSize = true
            };

            this.Controls.Add(lblLang);

            var langBox = new ComboBox
            {
                Location = new Point(486, 118),
                Width = 160,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            langBox.Items.Add("English");
            langBox.SelectedItem = opts.Language;
            this.Controls.Add(langBox);

            var chkAutoUpdate = new CheckBox
            {
                Text = "Enable Auto Update",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Location = new Point(486, 142),
                Width = 160,
                Checked = opts.AutoUpdate
            };

            chkAutoUpdate.CheckedChanged += (s, e) =>
            {
                if (!chkAutoUpdate.Checked)
                {
                    var result = MessageBox.Show(
                        "Are you sure you want to disable Auto Update?\n\n" +
                        "This ensures Launcher is up to date\n" +
                        "Current versions of the game may no longer avalible to you",
                        "Confirm",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result == DialogResult.No)
                    {
                        chkAutoUpdate.Checked = true;
                    }
                }
            };

            this.Controls.Add(chkAutoUpdate);

            var btnChangeMusic = new Button
            {
                Text = "Change Music",
                Width = 160,
                Location = new Point(486, 169),
                Font = new Font("Segoe UI", 12, FontStyle.Regular)
            };
            StyleButton(btnChangeMusic);

            btnChangeMusic.Click += (s, e) =>
            {
                string resDir = EnsureResourceFolder(AppDomain.CurrentDomain.BaseDirectory);
                string wavPath = Path.Combine(resDir, "Launcher_Music.wav");

                var result = MessageBox.Show(
                    "Do you want to restore the default launcher music?",
                    "Music Options",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                bool changed = false;

                if (result == DialogResult.Yes)
                {
                    if (File.Exists(wavPath))
                        File.Delete(wavPath);

                    changed = true;
                }
                else
                {
                    using (var dialog = new OpenFileDialog())
                    {
                        dialog.Filter = "WAV Files (*.wav)|*.wav";
                        dialog.Title = "Select Launcher Music";

                        if (dialog.ShowDialog() == DialogResult.OK)
                        {
                            File.Copy(dialog.FileName, wavPath, overwrite: true);
                            changed = true;
                        }
                    }
                }

                if (!changed)
                    return;

                var restart = MessageBox.Show(
                    "The launcher needs to restart to apply music changes.\nRestart now?",
                    "Restart Required",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information
                );

                if (restart == DialogResult.Yes)
                {
                    Process.Start(Application.ExecutablePath);
                }
            };

            this.Controls.Add(btnChangeMusic);

            var saveBtn = new Button
            {
                Text = "Save",
                Width = 214,
                Height = 52,
                Location = new Point(459, 274)
            };
            StyleButton(saveBtn);

            saveBtn.Click += (s, e) =>
            {
                opts.Language = langBox.SelectedItem.ToString();
                opts.AutoUpdate = chkAutoUpdate.Checked;
                SaveOptions(opts);
                MessageBox.Show("Options saved.");
                RebuildMainUI();
            };

            this.Controls.Add(saveBtn);

            saveBtn.Parent.Controls.Add(overlay);
            overlay.SendToBack();

            this.ResumeLayout(false);
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

            UpdatePatchButtonImage();
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
                bool restored = false;

                if (inst.LoaderType == "MelonLoader")
                {
                    string disabled = Path.Combine(currentDirectory, "_DISABLED_MELONLOADER");

                    if (Directory.Exists(disabled))
                    {
                        foreach (var dir in Directory.GetDirectories(disabled))
                            SafeMoveDirectory(dir, currentDirectory);

                        foreach (var file in Directory.GetFiles(disabled))
                            SafeMove(file, currentDirectory);

                        Directory.Delete(disabled, true);
                        restored = true;
                    }
                }
                else if (inst.LoaderType == "BepInEx")
                {
                    string disabled = Path.Combine(currentDirectory, "_DISABLED_BEPINEX");

                    if (Directory.Exists(disabled))
                    {
                        foreach (var dir in Directory.GetDirectories(disabled))
                            SafeMoveDirectory(dir, currentDirectory);

                        foreach (var file in Directory.GetFiles(disabled))
                            SafeMove(file, currentDirectory);

                        Directory.Delete(disabled, true);
                        restored = true;
                    }
                }

                if (!restored)
                {
                    if (inst.LoaderType == "MelonLoader")
                        ExtractMelonLoader(currentDirectory);
                    else if (inst.LoaderType == "BepInEx")
                        ExtractBepInEx(currentDirectory);
                }

                RefreshInstallationMetadata(inst);
                SaveInstallationInfo(inst);
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
                if (inst.LoaderType == "MelonLoader")
                {
                    string disabled = Path.Combine(path, "_DISABLED_MELONLOADER");
                    Directory.CreateDirectory(disabled);

                    SafeMoveDirectory(Path.Combine(path, "MelonLoader"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "Mods"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "Plugins"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "UserData"), disabled);
                    SafeMoveDirectory(Path.Combine(path, "UserLibs"), disabled);

                    SafeMove(Path.Combine(path, "version.dll"), disabled);
                    SafeMove(Path.Combine(path, "InstallationInfo.json"), disabled);
                }
                else if (inst.LoaderType == "BepInEx")
                {
                    string disabled = Path.Combine(path, "_DISABLED_BEPINEX");
                    Directory.CreateDirectory(disabled);

                    SafeMoveDirectory(Path.Combine(path, "BepInEx"), disabled);

                    SafeMove(Path.Combine(path, "winhttp.dll"), disabled);
                    SafeMove(Path.Combine(path, "doorstop_config.ini"), disabled);
                    SafeMove(Path.Combine(path, ".doorstop_version"), disabled);
                    SafeMove(Path.Combine(path, "InstallationInfo.json"), disabled);
                }

                RefreshInstallationMetadata(inst);
                SaveInstallationInfo(inst);
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

        private void btnModPacks_Click(object sender, EventArgs e)
        {
            var inst = installPaths.FirstOrDefault(
                i => string.Equals(i.Path, currentDirectory, StringComparison.OrdinalIgnoreCase)
            );

            if (inst == null)
            {
                MessageBox.Show("Installation not found. Add one first.");
                return;
            }

            ShowModpackManager(inst);
        }

        private void btnCommunity_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://discord.com/invite/DPAC5ZVJ8T",
                UseShellExecute = true
            });
        }

        private void btnCredit_Click(object sender, EventArgs e)
        {
            ShowCreditMenu();
        }

        private void btnOption_Click(object sender, EventArgs e)
        {
            ShowOptionsMenu();
        }

        private void btnQuit_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }
    }
}
