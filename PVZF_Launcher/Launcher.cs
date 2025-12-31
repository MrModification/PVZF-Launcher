using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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
        private readonly string launcherDirectory = AppDomain.CurrentDomain.BaseDirectory;

        private string installListFile;

        public class LauncherOptions
        {
            public string Language { get; set; } = "English";
            public bool AutoUpdate { get; set; } = true;
        }

        private async Task<string> DownloadWithSplashAsync(string url, string fileName)
        {
            string output = Path.Combine(Path.GetTempPath(), fileName);

            Splash splash = new Splash("Starting download...");
            splash.Show();

            using (var client = new WebClient())
            {
                client.DownloadProgressChanged += (s, e) =>
                {
                    if (e.TotalBytesToReceive > 0)
                    {
                        splash.UpdateMessage(
                            $"Do not close\n"
                                + $"Downloading {fileName}\n"
                                + $"{e.ProgressPercentage}%"
                        );
                    }
                    else
                    {
                        splash.UpdateMessage(
                            $"Do not close\n"
                                + $"Downloading {fileName}\n"
                                + $"(Server did not report file size)"
                        );
                    }
                };

                try
                {
                    await client.DownloadFileTaskAsync(new Uri(url), output);

                    splash.UpdateMessage("Download complete!");
                    await Task.Delay(500);
                }
                finally
                {
                    splash.Close();
                }
            }

            return output;
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
            public bool IsEnabled { get; set; } = true;
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
            public bool MetadataInitialized { get; set; }

            public string Version { get; set; }

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
        private void CleanupEmptyDirectories(string root, string path)
        {
            string dir = Path.GetDirectoryName(path);

            while (
                !string.IsNullOrEmpty(dir)
                && dir.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            )
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
                else
                {
                    break;
                }
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

                if (!hasMelonMods && !hasBepInEx)
                    throw new Exception(
                        "Modpack must contain either GameDirectory/Mods/ or GameDirectory/BepInEx/."
                    );

                if (
                    hasMelonMods
                    && !hasBepInEx
                    && !loaderType.Equals("MelonLoader", StringComparison.OrdinalIgnoreCase)
                )
                    throw new Exception(
                        "This modpack is for MelonLoader, but this installation uses "
                            + loaderType
                            + "."
                    );

                if (
                    hasBepInEx
                    && !hasMelonMods
                    && !loaderType.Equals("BepInEx", StringComparison.OrdinalIgnoreCase)
                )
                    throw new Exception(
                        "This modpack is for BepInEx, but this installation uses "
                            + loaderType
                            + "."
                    );

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
            string modsRoot = Path.Combine(inst.Path, "Mods");
            Directory.CreateDirectory(modsRoot);

            string downloadsRoot = Path.Combine(modsRoot, "ModDownloads");
            Directory.CreateDirectory(downloadsRoot);

            string sandbox = Path.Combine(downloadsRoot, "ModpackTemp_" + Guid.NewGuid());
            Directory.CreateDirectory(sandbox);

            ZipFile.ExtractToDirectory(pack.SourceFile, sandbox);

            string gameDir = Path.Combine(sandbox, "GameDirectory");
            if (!Directory.Exists(gameDir))
                throw new InvalidOperationException("Modpack is missing GameDirectory folder.");

            bool isBepInEx = inst.LoaderType.Equals("BepInEx", StringComparison.OrdinalIgnoreCase);
            bool isMelon = inst.LoaderType.Equals(
                "MelonLoader",
                StringComparison.OrdinalIgnoreCase
            );

            List<string> installedFiles = new List<string>();

            foreach (var file in Directory.GetFiles(gameDir, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(gameDir.Length + 1).Replace("\\", "/");

                if (isBepInEx && relative.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isMelon && relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (relative.Contains(".."))
                    continue;

                string dst = Path.Combine(inst.Path, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(file, dst, true);

                installedFiles.Add(relative);
            }

            pack.InstalledFiles = installedFiles;

            inst.InstalledModpacks.Add(pack);
            inst.ModCount = inst.InstalledModpacks.Count;

            SaveInstallationInfo(inst);

            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, true);
        }

        public void UninstallModpack(InstallationInfo inst, ModPackInfo pack)
        {
            string baseGameDir = inst.Path;

            if (inst.LoaderType == "MelonLoader")
            {
                string disabled = Path.Combine(inst.Path, "_DISABLED_MELONLOADER");
                if (Directory.Exists(disabled))
                    baseGameDir = disabled;
            }
            else if (inst.LoaderType == "BepInEx")
            {
                string disabled = Path.Combine(inst.Path, "_DISABLED_BEPINEX");
                if (Directory.Exists(disabled))
                    baseGameDir = disabled;
            }

            var protectedFiles = inst.InstalledModpacks
                .Where(p => p != pack)
                .SelectMany(p => p.InstalledFiles)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in pack.InstalledFiles)
            {
                if (protectedFiles.Contains(file))
                    continue;

                string full = Path.Combine(baseGameDir, file);
                if (File.Exists(full))
                {
                    File.Delete(full);
                    CleanupEmptyDirectories(inst.Path, full);
                }
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

                    string tempDir = Path.Combine(
                        Path.GetTempPath(),
                        "modpack_extract_" + Guid.NewGuid()
                    );
                    Directory.CreateDirectory(tempDir);

                    ZipFile.ExtractToDirectory(modpackZip, tempDir);

                    string gameDir = Path.Combine(tempDir, "GameDirectory");
                    if (!Directory.Exists(gameDir))
                        throw new Exception("Modpack is missing GameDirectory after extraction.");

                    pack.InstalledFiles = new List<string>();

                    foreach (
                        var file in Directory.GetFiles(gameDir, "*", SearchOption.AllDirectories)
                    )
                    {
                        string relative = file.Substring(gameDir.Length + 1);
                        string dst = Path.Combine(inst.Path, relative);

                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Copy(file, dst, true);

                        pack.InstalledFiles.Add(relative);
                    }

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

        public void DisableModpack(InstallationInfo inst, ModPackInfo pack)
        {
            string disabledDir = Path.Combine(inst.Path, "_DISABLED_" + inst.LoaderType.ToUpper());
            Directory.CreateDirectory(disabledDir);

            foreach (var file in pack.InstalledFiles)
            {
                string src = Path.Combine(inst.Path, file);
                string dst = Path.Combine(disabledDir, file);

                if (File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst))
                        File.Delete(dst);

                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Move(src, dst);
                    CleanupEmptyDirectories(inst.Path, src);
                }
            }

            pack.IsEnabled = false;
            SaveInstallationInfo(inst);
        }

        public void EnableModpack(InstallationInfo inst, ModPackInfo pack)
        {
            string disabledDir = Path.Combine(inst.Path, "_DISABLED_" + inst.LoaderType.ToUpper());

            foreach (var file in pack.InstalledFiles)
            {
                string src = Path.Combine(disabledDir, file);
                string dst = Path.Combine(inst.Path, file);

                if (File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (File.Exists(dst))
                        File.Delete(dst);

                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Move(src, dst);
                    CleanupEmptyDirectories(inst.Path, src);
                }
            }

            pack.IsEnabled = true;
            SaveInstallationInfo(inst);
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
            string resDir = Path.Combine(launcherDirectory, "PVZFL_Resources");
            string imagePath = Path.Combine(resDir, "Background.png");

            if (!File.Exists(imagePath))
                UnpackBackground(launcherDirectory);
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
            string path = Path.Combine(launcherDirectory, "PVZFL_Resources", fileName);

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
            UnpackButtonImage(launcherDirectory, "Launch.png", Properties.Resources.Launch);
            UnpackButtonImage(launcherDirectory, "Unpatch.png", Properties.Resources.Unpatch);
            UnpackButtonImage(launcherDirectory, "Patch.png", Properties.Resources.Patch);
            UnpackButtonImage(launcherDirectory, "ModPacks.png", Properties.Resources.ModPacks);
            UnpackButtonImage(
                launcherDirectory,
                "ManageInstalls.png",
                Properties.Resources.ManageInstalls
            );
            UnpackButtonImage(
                launcherDirectory,
                "CreateInstall.png",
                Properties.Resources.CreateInstall
            );
            UnpackButtonImage(
                launcherDirectory,
                "CommunityServer.png",
                Properties.Resources.Discord
            );
            UnpackButtonImage(
                launcherDirectory,
                "CreditPlant.png",
                Properties.Resources.CreditPlant
            );
            UnpackButtonImage(
                launcherDirectory,
                "OptionPlant.png",
                Properties.Resources.OptionPlant
            );
            UnpackButtonImage(launcherDirectory, "QuitPlant.png", Properties.Resources.QuitPlant);

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
            Task.Run(
                () =>
                {
                    foreach (var inst in installPaths)
                    {
                        inst.MetadataInitialized = false;
                        RefreshInstallationMetadata(inst);
                    }

                    SaveInstallPaths();
                }
            );

            SelectDefaultInstallation();
            UpdateBrowseButtonImage();
            UpdatePatchButtonImage();

            RebuildMainUI();
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
        private string GetGameExe(string path)
        {
            if (!Directory.Exists(path))
                return null;

            return Directory
                .GetFiles(path, "*.exe", SearchOption.AllDirectories)
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
        }
        private string GetGameRoot(string path)
        {
            string exe = GetGameExe(path);
            return exe == null ? null : Path.GetDirectoryName(exe);
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

        public static string MakeRelativePath(string basePath, string fullPath)
        {
            Uri baseUri = new Uri(basePath.EndsWith("\\") ? basePath : basePath + "\\");
            Uri fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(
                baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', '\\')
            );
        }

        public void InstallTranslationMod(
            string translationZip,
            string installPath,
            InstallationInfo inst
        )
        {
            string modsRoot = Path.Combine(installPath, "Mods");
            Directory.CreateDirectory(modsRoot);

            string downloadsRoot = Path.Combine(modsRoot, "ModDownloads");
            Directory.CreateDirectory(downloadsRoot);

            string sandbox = Path.Combine(downloadsRoot, "TranslatorTemp_" + Guid.NewGuid());
            Directory.CreateDirectory(sandbox);

            ZipFile.ExtractToDirectory(translationZip, sandbox);

            string translatorFolderPath = null;

            foreach (var file in Directory.GetFiles(sandbox, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith("PvZ_Fusion_Translator.dll", StringComparison.OrdinalIgnoreCase))
                {
                    translatorFolderPath = Path.GetDirectoryName(file);
                    break;
                }
            }

            if (translatorFolderPath == null)
                throw new InvalidOperationException(
                    "Invalid TranslationMod.zip – PvZ_Fusion_Translator.dll not found."
                );

            List<string> installedFiles = new List<string>();

            foreach (
                var file in Directory.GetFiles(
                    translatorFolderPath,
                    "*",
                    SearchOption.AllDirectories
                )
            )
            {
                string relative = file.Substring(translatorFolderPath.Length + 1)
                    .Replace("\\", "/");
                string outPath = Path.Combine(modsRoot, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.Copy(file, outPath, true);

                installedFiles.Add(MakeRelativePath(installPath, outPath));
            }

            var pack = new ModPackInfo
            {
                Name = "PvZ_Fusion_Translator",
                Creator = "Blooms",
                LoaderType = inst.LoaderType,
                SourceFile = translationZip,
                InstalledFiles = installedFiles,
                IsEnabled = true
            };

            inst.InstalledModpacks.Add(pack);
            inst.ModCount = inst.InstalledModpacks.Count;

            SaveInstallationInfo(inst);

            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, true);

            if (File.Exists(translationZip))
                File.Delete(translationZip);
        }

        public async Task InstallModManager(string installPath, InstallationInfo inst)
        {
            string apiUrl = "https://api.github.com/repos/MrModification/PVZF-ModManager/releases";

            JArray releases;
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("PVZF-Installer");
                string json = await client.GetStringAsync(apiUrl);
                releases = JArray.Parse(json);
            }

            var release = releases.FirstOrDefault(
                r =>
                    string.Equals(
                        (string)r["tag_name"],
                        inst.Version,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

            if (release == null)
            {
                MessageBox.Show(
                    $"Mod Manager is not available for version {inst.Version}.",
                    "Not Available",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            var asset = release["assets"]?.FirstOrDefault();
            if (asset == null)
            {
                MessageBox.Show(
                    $"Mod Manager release for version {inst.Version} has no downloadable assets.",
                    "Invalid Release",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            string downloadUrl = (string)asset["browser_download_url"];
            string fileName = (string)asset["name"];

            string zipPath = await DownloadWithSplashAsync(downloadUrl, fileName);

            string modsRoot = Path.Combine(installPath, "Mods");
            Directory.CreateDirectory(modsRoot);

            string downloadsRoot = Path.Combine(modsRoot, "ModDownloads");
            Directory.CreateDirectory(downloadsRoot);

            string sandbox = Path.Combine(downloadsRoot, "ModManagerTemp_" + Guid.NewGuid());
            Directory.CreateDirectory(sandbox);

            ZipFile.ExtractToDirectory(zipPath, sandbox);

            List<string> installedFiles = new List<string>();

            bool isBepInEx = inst.LoaderType.Equals("BepInEx", StringComparison.OrdinalIgnoreCase);
            bool isMelon = inst.LoaderType.Equals(
                "MelonLoader",
                StringComparison.OrdinalIgnoreCase
            );

            foreach (var file in Directory.GetFiles(sandbox, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(sandbox.Length + 1).Replace("\\", "/");

                if (isBepInEx && relative.StartsWith("Mods/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (isMelon && relative.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase))
                    continue;

                string outPath = Path.Combine(installPath, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.Copy(file, outPath, true);

                installedFiles.Add(MakeRelativePath(installPath, outPath));
            }

            var pack = new ModPackInfo
            {
                Name = "PVZF-Mod-Manager",
                Creator = "MrModification",
                LoaderType = inst.LoaderType,
                InstalledFiles = installedFiles,
                SourceFile = zipPath,
                IsEnabled = true
            };

            inst.InstalledModpacks.Add(pack);
            inst.ModCount = inst.InstalledModpacks.Count;

            SaveInstallationInfo(inst);

            if (Directory.Exists(sandbox))
                Directory.Delete(sandbox, true);

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            MessageBox.Show("Mod Manager installed successfully!");
        }

        bool RequiresNet6(string versionString)
        {
            Version v = new Version(versionString);
            return v >= new Version("3.1.1");
        }

        private bool IsSafeToDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string full = Path.GetFullPath(path).TrimEnd('\\');
            string root = Path.GetPathRoot(full).TrimEnd('\\');

            if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                return false;

            string[] forbidden =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            foreach (var f in forbidden)
            {
                if (string.Equals(full, f.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            string exe = GetGameExe(path);
            if (exe == null)
                return false;

            return true;
        }
        private void UninstallInstallation(InstallationInfo inst)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to uninstall the installation:\n\n{inst.DisplayName}\n\n"
                    + "This will permanently delete the entire installation folder, including all mods and modpacks.",
                "Confirm Uninstall Installation",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (result != DialogResult.Yes)
                return;

            try
            {
                string gameRoot = GetGameRoot(inst.Path);

                if (gameRoot == null)
                {
                    MessageBox.Show(
                        "Unable to locate a valid game installation. Aborting uninstall."
                    );
                    return;
                }

                gameRoot = Path.GetFullPath(gameRoot).TrimEnd('\\');

                if (!IsSafeToDelete(gameRoot))
                {
                    MessageBox.Show(
                        "The resolved installation path appears unsafe. Aborting uninstall."
                    );
                    return;
                }

                var confirm = MessageBox.Show(
                    $"The following folder will be permanently deleted:\n\n{gameRoot}\n\n"
                        + "Are you absolutely sure?",
                    "Final Confirmation",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning
                );

                if (confirm != DialogResult.Yes)
                    return;

                if (Directory.Exists(gameRoot))
                    Directory.Delete(gameRoot, true);

                installPaths.Remove(inst);
                installPaths.RemoveAll(i => !Directory.Exists(i.Path));

                SaveInstallPaths();

                MessageBox.Show("Installation uninstalled successfully.");
                RebuildMainUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to uninstall installation:\n" + ex.Message);
            }
        }

        private async Task CreateNewInstallationFromLocalZip(
            string version,
            string loader,
            string zipPath,
            string modpackZipPath,
            string language,
            string selectedNet6Zip,
            string selectedTranslationZip,
            bool installModManager
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

                InstallationInfo inst =
                    LoadInstallationInfo(installPath) ?? CreateInstallationInfo(installPath);

                inst.Version = version;

                if (loader == "MelonLoader")
                    ExtractMelonLoader(installPath);
                else if (loader == "BepInEx")
                    ExtractBepInEx(installPath);

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
                        int index = lines.FindIndex(l => l.Trim() == "[PvZ_Fusion_Translator]");

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
                            InstallNet6(selectedNet6Zip, installPath);

                        InstallTranslationMod(selectedTranslationZip, installPath, inst);
                    }
                }

                if (installModManager == true)
                {
                    await InstallModManager(installPath, inst);
                }

                if (!string.IsNullOrEmpty(modpackZipPath))
                {
                    ModPackInfo pack = ValidateModpack(modpackZipPath, loader);

                    var conflicts = GetConflicts(inst, pack);
                    if (conflicts.Count > 0)
                    {
                        string preview = string.Join("\n", conflicts.Take(20));

                        var result = MessageBox.Show(
                            $"Warning: This modpack will overwrite {conflicts.Count} files:\n\n{preview}\n\n"
                                + "Do you want to continue?",
                            "Modpack Conflict",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning
                        );

                        if (result == DialogResult.No)
                            return;
                    }
                    else
                    {
                        var result = MessageBox.Show(
                            $"Install modpack '{pack.Name}'?",
                            "Confirm Installation",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question
                        );

                        if (result == DialogResult.No)
                            return;
                    }
                    InstallModpack(inst, pack);
                    SaveInstallationInfo(inst);
                }
                bool exists = installPaths.Any(
                    i => string.Equals(i.Path, installPath, StringComparison.OrdinalIgnoreCase)
                );

                if (!exists)
                {
                    installPaths.Add(inst);
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
            if (string.IsNullOrWhiteSpace(auto))
                return;

            string root = GetGameRoot(auto);
            if (root == null)
                return;

            string normalized = Path.GetFullPath(root).TrimEnd('\\');

            bool exists = installPaths.Any(
                i =>
                    string.Equals(
                        Path.GetFullPath(i.Path).TrimEnd('\\'),
                        normalized,
                        StringComparison.OrdinalIgnoreCase
                    )
            );

            if (!exists)
            {
                var info = CreateInstallationInfo(normalized);
                info.MetadataInitialized = true;
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
                    var info = LoadInstallationInfo(inst.Path);
                    if (info != null)
                    {
                        inst.InstalledModpacks = info.InstalledModpacks ?? new List<ModPackInfo>();
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
            string root = GetGameRoot(path);
            if (root == null)
                return null;

            var info = new InstallationInfo { Path = root };
            RefreshInstallationMetadata(info);
            return info;
        }

        private void RefreshInstallationMetadata(InstallationInfo info)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Path))
                return;

            if (info.MetadataInitialized)
                return;

            try
            {
                string root = GetGameRoot(info.Path);
                if (root == null)
                {
                    info.ExeName = "";
                    info.LoaderType = "";
                    info.ModCount = 0;
                    info.IsPatched = false;
                    return;
                }

                string exe = GetGameExe(root);
                info.ExeName = exe != null ? Path.GetFileName(exe) : "";

                info.IsPatched = IsPatched(root);

                string type = GetPatchType(root);
                if (!string.IsNullOrEmpty(type))
                {
                    if (type == "Melon")
                        info.LoaderType = "MelonLoader";
                    else if (type == "Bep")
                        info.LoaderType = "BepInEx";
                }

                info.ModCount = CountMods(root, info.LoaderType);
            }
            catch
            {
                info.ExeName = "";
                info.LoaderType = "";
                info.ModCount = 0;
                info.IsPatched = false;
            }
            info.MetadataInitialized = true;
        }

        private int CountMods(string root, string loaderType)
        {
            try
            {
                string realRoot = GetGameRoot(root);
                if (realRoot == null)
                    return 0;

                string modsPath = null;

                if (loaderType == "BepInEx")
                    modsPath = Path.Combine(realRoot, "BepInEx", "Plugins");
                else
                    modsPath = Path.Combine(realRoot, "Mods");

                if (!Directory.Exists(modsPath))
                    return 0;

                var dlls = Directory.GetFiles(modsPath, "*.dll", SearchOption.AllDirectories);

                int count = 0;

                foreach (var dll in dlls)
                {
                    if (dll.Contains("_DISABLED_"))
                        continue;

                    count++;
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        private bool IsPatched(string installPath)
        {
            string root = GetGameRoot(installPath);
            if (root == null)
                return false;

            if (Directory.Exists(Path.Combine(root, "MelonLoader")))
                return true;
            if (File.Exists(Path.Combine(root, "version.dll")))
                return true;

            if (Directory.Exists(Path.Combine(root, "BepInEx")))
                return true;
            if (File.Exists(Path.Combine(root, "winhttp.dll")))
                return true;
            if (File.Exists(Path.Combine(root, "doorstop_config.ini")))
                return true;

            return false;
        }

        private string GetPatchType(string installPath)
        {
            string root = GetGameRoot(installPath);
            if (root == null)
                return "No";
            try
            {
                if (Directory.Exists(Path.Combine(root, "MelonLoader")))
                    return "Melon";

                if (Directory.Exists(Path.Combine(root, "BepInEx")))
                    return "Bep";

                return "No";
            }
            catch
            {
                return "No";
            }
        }

        private void SelectDefaultInstallation()
        {
            installPaths.RemoveAll(i => !Directory.Exists(i.Path));

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

                string realRoot = GetGameRoot(newPath);
                if (realRoot == null)
                {
                    MessageBox.Show("No valid game executable found in the selected directory.");
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
                inst.Path = realRoot;
                currentDirectory = realRoot;

                if (loaderChanged)
                {
                    if (IsPatched(realRoot))
                    {
                        MessageBox.Show("Please unpatch first.");
                    }
                    else
                    {
                        MessageBox.Show("Loader type changed. You can now patch.");
                        inst.LoaderType = newLoaderType;
                    }
                }

                inst.MetadataInitialized = false;
                RefreshInstallationMetadata(inst);

                SaveInstallationInfo(inst);
                SaveInstallPaths();

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
                        $"Name: {inst.DisplayName} | Mods: {inst.ModCount} | Patched: {GetPatchType(GetGameRoot(inst.Path))} | Last Played: {lastPlayedText}",
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
                    var result = MessageBox.Show(
                        "Do you want to completely uninstall this installation,\n"
                            + "or just remove it from your launcher list?\n\n"
                            + "Yes = Uninstall installation\n"
                            + "No = Remove from list only\n"
                            + "Cancel = Do nothing",
                        "Remove Installation",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.Cancel)
                        return;

                    if (result == DialogResult.No)
                    {
                        installPaths.Remove(inst);
                        SaveInstallPaths();
                        ShowInstallationManager();
                        return;
                    }

                    if (result == DialogResult.Yes)
                    {
                        try
                        {
                            UninstallInstallation(inst);
                            installPaths.Remove(inst);
                            SaveInstallPaths();
                            ShowInstallationManager();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Failed to uninstall installation:\n" + ex.Message);
                        }
                    }
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

            var store = LoadInstallationStore();

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
                        string root = GetGameRoot(path);

                        if (root == null)
                        {
                            MessageBox.Show(
                                "No valid game executable found in this folder.",
                                "Invalid Installation",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error
                            );
                            return;
                        }

                        if (
                            installPaths.Any(
                                i =>
                                    string.Equals(
                                        Path.GetFullPath(i.Path).TrimEnd('\\'),
                                        Path.GetFullPath(root).TrimEnd('\\'),
                                        StringComparison.OrdinalIgnoreCase
                                    )
                            )
                        )
                        {
                            MessageBox.Show(
                                "This installation is already added.",
                                "Duplicate Installation",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information
                            );
                            return;
                        }

                        using (var versionDialog = new Form())
                        {
                            versionDialog.Text = "Select Game Version";
                            versionDialog.Size = new Size(350, 180);
                            versionDialog.StartPosition = FormStartPosition.CenterParent;
                            versionDialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                            versionDialog.MaximizeBox = false;
                            versionDialog.MinimizeBox = false;

                            Label lbl = new Label
                            {
                                Text = "Choose the version for this installation:",
                                AutoSize = true,
                                Location = new Point(15, 15)
                            };
                            versionDialog.Controls.Add(lbl);

                            ComboBox versionBox = new ComboBox
                            {
                                DropDownStyle = ComboBoxStyle.DropDownList,
                                Location = new Point(15, 45),
                                Width = 300
                            };

                            if (store != null && store.Versions.Count > 0)
                            {
                                foreach (var entry in store.Versions)
                                    versionBox.Items.Add(entry.Version);
                            }

                            versionBox.Items.Add("Other");

                            if (versionBox.Items.Count > 0)
                                versionBox.SelectedIndex = 0;

                            versionDialog.Controls.Add(versionBox);

                            Button ok = new Button
                            {
                                Text = "OK",
                                DialogResult = DialogResult.OK,
                                Location = new Point(15, 85),
                                Width = 300
                            };
                            versionDialog.Controls.Add(ok);

                            versionDialog.AcceptButton = ok;

                            if (versionDialog.ShowDialog() != DialogResult.OK)
                                return;

                            string selectedVersion = versionBox.SelectedItem.ToString();

                            var info = CreateInstallationInfo(root);
                            info.Version = selectedVersion;

                            installPaths.Add(info);
                            SaveInstallPaths();
                            ShowInstallationManager();
                        }
                    }
                }
            };
            this.Controls.Add(add);

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
                                info.MetadataInitialized = false;
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

                var modmgr = MessageBox.Show(
                    "Would you like to install Mod Manager/Store as part of this installation?",
                    "Install Mod Manager",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );

                bool installModManager = (modmgr == DialogResult.Yes);

                await CreateNewInstallationFromLocalZip(
                    version,
                    loader,
                    selectedVersionZip,
                    selectedModpackZip,
                    language,
                    selectedNet6Zip,
                    selectedTranslationZip,
                    installModManager
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

        private string GetPackSize(
            List<string> installedFiles,
            string installRoot,
            string loaderType
        )
        {
            if (installedFiles == null || installedFiles.Count == 0)
                return "0 B";

            long totalBytes = 0;

            string disabledRoot = Path.Combine(installRoot, "_DISABLED_" + loaderType.ToUpper());

            foreach (var relative in installedFiles)
            {
                try
                {
                    string enabledPath = Path.Combine(installRoot, relative);
                    string disabledPath = Path.Combine(disabledRoot, relative);

                    if (File.Exists(enabledPath))
                        totalBytes += new FileInfo(enabledPath).Length;
                    else if (File.Exists(disabledPath))
                        totalBytes += new FileInfo(disabledPath).Length;
                }
                catch { }
            }

            double size = totalBytes;

            if (size < 1024)
                return $"{size:0} B";
            size /= 1024;

            if (size < 1024)
                return $"{size:0.0} KB";
            size /= 1024;

            if (size < 1024)
                return $"{size:0.0} MB";
            size /= 1024;

            return $"{size:0.00} GB";
        }

        private void ShowModListForPack(InstallationInfo inst, ModPackInfo pack)
        {
            this.Controls.Clear();
            this.BackgroundImage = BytesToImage(Properties.Resources.Menu);
            this.BackgroundImageLayout = ImageLayout.Stretch;
            string sizeText = GetPackSize(
                pack.InstalledFiles,
                GetGameRoot(inst.Path),
                inst.LoaderType
            );

            Label title = new Label
            {
                Text = $"Mods in {pack.Name}  ({sizeText})",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 60, 60),
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Location = new Point(20, 20),
                AutoSize = true
            };
            this.Controls.Add(title);

            Panel listPanel = new Panel
            {
                Location = new Point(20, 50),
                Size = new Size(this.ClientSize.Width - 40, this.ClientSize.Height - 140),
                AutoScroll = true,
                BackColor = Color.FromArgb(30, 30, 30),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(listPanel);

            var dlls = pack.InstalledFiles
                .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var dll in dlls)
            {
                Panel row = new Panel
                {
                    Height = 50,
                    Dock = DockStyle.Top,
                    BackColor = Color.FromArgb(50, 50, 50)
                };

                Label lbl = new Label
                {
                    Text = Path.GetFileName(dll),
                    ForeColor = Color.White,
                    Location = new Point(10, 15),
                    AutoSize = true
                };

                Button toggle = new Button { Width = 120, Height = 30 };
                StyleButton(toggle);

                string fullPath = Path.Combine(inst.Path, dll);
                string disabledPath = Path.Combine(
                    inst.Path,
                    "_DISABLED_" + inst.LoaderType.ToUpper(),
                    dll
                );

                bool isEnabled = File.Exists(fullPath);

                if (isEnabled)
                {
                    toggle.Text = "Disable";
                    toggle.BackColor = Color.DarkOrange;
                }
                else
                {
                    toggle.Text = "Enable";
                    toggle.BackColor = Color.Green;
                }

                toggle.Click += (s, e) =>
                {
                    if (isEnabled)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(disabledPath));
                        if (File.Exists(disabledPath))
                            File.Delete(disabledPath);
                        File.Move(fullPath, disabledPath);

                        CleanupEmptyDirectories(inst.Path, fullPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                        if (File.Exists(fullPath))
                            File.Delete(fullPath);
                        File.Move(disabledPath, fullPath);

                        string disabledRoot = Path.Combine(
                            inst.Path,
                            "_DISABLED_" + inst.LoaderType.ToUpper()
                        );
                        CleanupEmptyDirectories(disabledRoot, disabledPath);
                    }

                    ShowModListForPack(inst, pack);
                };

                row.Controls.Add(lbl);
                row.Controls.Add(toggle);

                toggle.Location = new Point(row.Width - toggle.Width - 10, 10);
                row.Resize += (s, e) =>
                {
                    toggle.Left = row.Width - toggle.Width - 10;
                };

                listPanel.Controls.Add(row);
                listPanel.Controls.SetChildIndex(row, 0);
            }

            Button back = new Button
            {
                Text = "Back",
                Width = 200,
                Height = 40,
                Location = new Point(this.ClientSize.Width - 220, this.ClientSize.Height - 80)
            };
            StyleButton(back);

            back.Click += (s, e) => ShowModpackManager(inst);

            this.Controls.Add(back);
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

            foreach (var pack in inst.InstalledModpacks.ToList())
            {
                bool isDisabled = !pack.IsEnabled;

                Panel row = new Panel
                {
                    Height = 80,
                    Dock = DockStyle.Top,
                    BackColor = Color.FromArgb(50, 50, 50)
                };

                string sizeText = GetPackSize(
                    pack.InstalledFiles,
                    GetGameRoot(inst.Path),
                    inst.LoaderType
                );

                Label lbl = new Label
                {
                    Text =
                        $"[{pack.LoaderType}] {pack.Name} by {pack.Creator} — {pack.InstalledFiles.Count} files — {sizeText}"
                        + (isDisabled ? " (DISABLED)" : ""),
                    ForeColor = isDisabled ? Color.Gray : Color.White,
                    Location = new Point(10, 10),
                    AutoSize = true
                };

                var dlls = pack.InstalledFiles
                    .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .ToList();

                string dllText;

                if (dlls.Count == 0)
                {
                    dllText = "Mods: (none)";
                }
                else if (dlls.Count <= 2)
                {
                    dllText = "Mods: " + string.Join(", ", dlls);
                }
                else
                {
                    dllText = "Mods: " + string.Join(", ", dlls.Take(2)) + ", ...";
                }

                Label dllLabel = new Label
                {
                    Text = dllText,
                    ForeColor = Color.LightGray,
                    Location = new Point(10, 35),
                    AutoSize = true
                };

                Button uninstall = new Button { Text = "Uninstall", Width = 120, Height = 35 };
                StyleButton(uninstall);
                uninstall.BackColor = Color.DarkRed;

                uninstall.Enabled = !isDisabled;
                if (isDisabled)
                    uninstall.BackColor = Color.FromArgb(80, 80, 80);

                uninstall.Click += (s, e) =>
                {
                    var result = MessageBox.Show(
                        $"Are you sure you want to uninstall '{pack.Name}'?\n\n"
                            + "All files belonging to this modpack will be removed.",
                        "Confirm Uninstall",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result != DialogResult.Yes)
                        return;

                    UninstallModpack(inst, pack);
                    SaveInstallationInfo(inst);
                    ShowModpackManager(inst);
                };

                Button toggle = new Button { Width = 120, Height = 35 };
                StyleButton(toggle);

                if (pack.IsEnabled)
                {
                    toggle.Text = "Disable";
                    toggle.BackColor = Color.DarkOrange;
                }
                else
                {
                    toggle.Text = "Enable";
                    toggle.BackColor = Color.Green;
                }

                toggle.Click += (s, e) =>
                {
                    if (pack.IsEnabled)
                        DisableModpack(inst, pack);
                    else
                        EnableModpack(inst, pack);

                    SaveInstallationInfo(inst);
                    ShowModpackManager(inst);
                };

                Button manage = new Button { Text = "Manage Mods", Width = 120, Height = 35 };
                StyleButton(manage);

                manage.Click += (s, e) =>
                {
                    ShowModListForPack(inst, pack);
                };

                row.Controls.Add(lbl);
                row.Controls.Add(dllLabel);
                row.Controls.Add(uninstall);
                row.Controls.Add(manage);
                row.Controls.Add(toggle);

                int buttonY = 35;
                uninstall.Location = new Point(row.Width - uninstall.Width - 10, buttonY);
                manage.Location = new Point(row.Width - manage.Width - 270, buttonY);
                toggle.Location = new Point(row.Width - toggle.Width - 140, buttonY);

                row.Resize += (s, e) =>
                {
                    uninstall.Left = row.Width - uninstall.Width - 10;
                    toggle.Left = row.Width - toggle.Width - 140;
                    manage.Left = row.Width - manage.Width - 270;
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
            add.BackColor = Color.FromArgb(40, 120, 40);

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

            bool hasModManager = inst.InstalledModpacks.Any(p => p.Name == "PVZF-Mod-Manager");

            if (!hasModManager)
            {
                Button installMM = new Button
                {
                    Text = "Install Mod Manager",
                    Width = 200,
                    Height = 40,
                    Location = new Point(this.ClientSize.Width - 440, this.ClientSize.Height - 80)
                };
                StyleButton(installMM);

                installMM.BackColor = Color.FromArgb(70, 130, 180);

                installMM.Click += async (s, e) =>
                {
                    await InstallModManager(inst.Path, inst);
                    ShowModpackManager(inst);
                };

                this.Controls.Add(installMM);
            }
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
                        "Are you sure you want to disable Auto Update?\n\n"
                            + "This ensures Launcher is up to date\n"
                            + "Current versions of the game may no longer avalible to you",
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

                Directory.CreateDirectory(destDir);

                string dest = Path.Combine(destDir, Path.GetFileName(source));

                if (File.Exists(dest))
                {
                    string backup = dest + ".backup_" + DateTime.Now.Ticks;
                    File.Move(dest, backup);
                }

                try
                {
                    File.Move(source, dest);
                }
                catch (IOException)
                {
                    File.Copy(source, dest, overwrite: true);
                    File.Delete(source);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SafeMove failed: " + ex.Message);
            }
        }

        private void SafeMoveDirectory(string sourceDir, string destDir)
        {
            try
            {
                if (!Directory.Exists(sourceDir))
                    return;

                Directory.CreateDirectory(destDir);

                string dest = Path.Combine(destDir, Path.GetFileName(sourceDir));

                if (Directory.Exists(dest))
                {
                    string backup = dest + "_backup_" + DateTime.Now.Ticks;
                    Directory.Move(dest, backup);
                }

                try
                {
                    Directory.Move(sourceDir, dest);
                }
                catch (IOException)
                {
                    CopyDirectory(sourceDir, dest);
                    Directory.Delete(sourceDir, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("SafeMoveDirectory failed: " + ex.Message);
            }
        }

        private void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string dest = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
            }
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

            string root = GetGameRoot(inst.Path);
            if (root == null)
            {
                MessageBox.Show("Invalid installation directory.");
                return;
            }

            try
            {
                bool restored = false;

                if (inst.LoaderType == "MelonLoader")
                {
                    string disabled = Path.Combine(root, "_DISABLED_MELONLOADER");

                    if (Directory.Exists(disabled))
                    {
                        foreach (var dir in Directory.GetDirectories(disabled))
                            SafeMoveDirectory(dir, root);

                        foreach (var file in Directory.GetFiles(disabled))
                            SafeMove(file, root);

                        Directory.Delete(disabled, true);
                        restored = true;
                    }
                }
                else if (inst.LoaderType == "BepInEx")
                {
                    string disabled = Path.Combine(root, "_DISABLED_BEPINEX");

                    if (Directory.Exists(disabled))
                    {
                        foreach (var dir in Directory.GetDirectories(disabled))
                            SafeMoveDirectory(dir, root);

                        foreach (var file in Directory.GetFiles(disabled))
                            SafeMove(file, root);

                        Directory.Delete(disabled, true);
                        restored = true;
                    }
                }

                if (!restored)
                {
                    if (inst.LoaderType == "MelonLoader")
                        ExtractMelonLoader(root);
                    else if (inst.LoaderType == "BepInEx")
                        ExtractBepInEx(root);
                }

                inst.MetadataInitialized = false;
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

            string root = GetGameRoot(inst.Path);
            if (root == null)
            {
                MessageBox.Show("Invalid installation directory.");
                return;
            }

            try
            {
                if (inst.LoaderType == "MelonLoader")
                {
                    string disabled = Path.Combine(root, "_DISABLED_MELONLOADER");
                    Directory.CreateDirectory(disabled);

                    SafeMoveDirectory(Path.Combine(root, "MelonLoader"), disabled);
                    SafeMoveDirectory(Path.Combine(root, "Mods"), disabled);
                    SafeMoveDirectory(Path.Combine(root, "Plugins"), disabled);
                    SafeMoveDirectory(Path.Combine(root, "UserData"), disabled);
                    SafeMoveDirectory(Path.Combine(root, "UserLibs"), disabled);

                    SafeMove(Path.Combine(root, "version.dll"), disabled);
                    SafeMove(Path.Combine(root, "InstallationInfo.json"), disabled);
                }
                else if (inst.LoaderType == "BepInEx")
                {
                    string disabled = Path.Combine(root, "_DISABLED_BEPINEX");
                    Directory.CreateDirectory(disabled);

                    SafeMoveDirectory(Path.Combine(root, "BepInEx"), disabled);

                    SafeMove(Path.Combine(root, "winhttp.dll"), disabled);
                    SafeMove(Path.Combine(root, "doorstop_config.ini"), disabled);
                    SafeMove(Path.Combine(root, ".doorstop_version"), disabled);
                    SafeMove(Path.Combine(root, "InstallationInfo.json"), disabled);
                }

                inst.MetadataInitialized = false;
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
            if (string.IsNullOrWhiteSpace(currentDirectory))
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
                string exePath = GetGameExe(currentDirectory);

                if (exePath == null)
                {
                    MessageBox.Show("No valid game executable found in:\n" + currentDirectory);
                    return;
                }

                string gameRoot = Path.GetDirectoryName(exePath);

                Process.Start(exePath);

                var inst = installPaths.FirstOrDefault(
                    i =>
                        string.Equals(
                            Path.GetFullPath(i.Path).TrimEnd('\\'),
                            Path.GetFullPath(gameRoot).TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase
                        )
                );

                if (inst != null)
                {
                    inst.LastPlayed = DateTime.Now;
                    SaveInstallPaths();
                }

                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to launch:\n" + ex.Message);
            }
        }

        private void btnModPacks_Click(object sender, EventArgs e)
        {
            string root = GetGameRoot(currentDirectory);
            if (root == null)
            {
                MessageBox.Show("Invalid installation directory.");
                return;
            }

            var inst = installPaths.FirstOrDefault(
                i =>
                    string.Equals(
                        Path.GetFullPath(i.Path).TrimEnd('\\'),
                        Path.GetFullPath(root).TrimEnd('\\'),
                        StringComparison.OrdinalIgnoreCase
                    )
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
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "https://discord.com/invite/DPAC5ZVJ8T",
                    UseShellExecute = true
                }
            );
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