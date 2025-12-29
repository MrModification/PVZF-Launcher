# 🌱 PvZ Fusion Launcher

A user‑friendly launcher for **Plants vs. Zombies Fusion**, designed to make installing, updating, and managing Fusion setups effortless.


The PvZ Fusion Launcher handles everything from file validation to mod loader integration, language selection, and modpack installation.


---



## ✨ Features



### 🚀 Easy Installation


1. Download the latest **PvZ Fusion Launcher** from the Releases page.

2. Run the launcher

3. Click "Create Installation"

4. Choose your Version

5. Choose your ModLoader

5. Pick a translation (optional)

5. Install a modpack (optional)

6. Click **Install** and let the launcher handle the rest.


---



### 📥 Bootstrapper

- Automatically detects last launced PvZF installation

- Self-Bootstrap (Installs to : AppData\\Local\\PVZF\_Launcher)

- Creates Desktop and Startmenu shortcuts

- To make Launcher.exe portable rename with _P or _Portable in the name



### 🔄 Automatic Updates

- Checks GitHub Releases for the latest builds

- Downloads and installs updates seamlessly

- Disable in options



### 🌍 PVZF-Translation Support

- Launcher currently English only

- Lets users choose their preferred language before installation of each instance



### 🫧 Quick Mod Loader Integration

- Detects existing mod loaders

- Ensures game instances install cleanly.

- Handles directory management and patch detection safely.



### 🧪 File Validation

- Detects missing or malformed files before installation.


---


### 📦 NEW!!! ModPacks

- Creating a .modpack is easy
- Create a folder name "GameDirectory"
- Inside the newly created folder put in Mods, BepInEx/Plugins, UserData or whatever else
- Outside "GameDirectory" create a "ModPack.json"
- Inside ModPack.json should be something like this:
```json
{
  "name": "ExamplePack",
  "creator": "TheInvisibleIce",
  "version": "1.0",
  "description": "Shows Simple Mod Pack Creation(Version,Description,Tags are best to have now, but currently don't do anything as mod manager is not yet finished/public)",
  "tags": ["utility"]
}
```
- Select "GameDirectory" and "ModPack.json" and put them in a .zip archive
- Rename the .zip to .Modpack
- Final structure for a MelonLoader pack should be like this:
```
ExamplePack.Modpack
│
├── ModPack.json
│
├── GameDirectory/
│   └── Mods/
│       ├── ExampleMod.dll
│       ├── SuperAwesomeCustomPlant.dll
│       ├── SuperAwesomeCustomZombie.dll
│       └── ...
```
- Final structure for a BepInEx pack should be like this:
```
ExamplePack.Modpack
│
├── ModPack.json
│
├── GameDirectory/
│   └── BepInEx/
│       └── plugins
│           ├── ExampleMod.dll
│           ├── SuperAwesomeCustomPlant.dll
│           ├── SuperAwesomeCustomZombie.dll
│           └── ...
```



---


## 🤝 Contributing


Pull requests are welcome.  

If you want to help improve the launcher — translations, UI improvements, bug fixes — feel free to open an issue or submit a Pull Request


---


## ❤️ Credits

PVZ Fusion Created by LanPiaoPiao and his team
- Original Developers of Game [Link to Bilibili](https://space.bilibili.com/3546619314178489)


The wonderful people that collectively built [PVZF-Translation](https://github.com/Teyliu/PVZF-Translation/blob/main/README.md?plain=1#L118)

Net6 Binaries by [Cassidy](https://github.com/SillyStar-Github)

Download Mirrors by @leo.tai_0325 discord
