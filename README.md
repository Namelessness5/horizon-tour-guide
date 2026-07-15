# Horizon Guide

**A tour-guide radio for the open road — built for Forza Horizon.**

*[English](#english) · [中文](#中文)*

---

## English

You're drifting through Forza Horizon's Japan. A torii gate stands out in the surf, a neon crossing floods past, a lonely hotel clings to a mountainside. And someone in the passenger seat — someone who actually knows the place — leans over and tells you its story.

That's Horizon Guide. It runs quietly alongside the game, watches where you drive, and the moment you roll into somewhere worth talking about, a voice comes on the radio and subtitles fade in at the bottom of the screen.

What makes it more than a wiki-on-wheels:

- **It knows the map is fiction.** Forza's Japan is compressed, rearranged, half-invented. Horizon Guide figures out what each in-game place is *actually based on* — a faithful copy (Shibuya Crossing, Kinkaku-ji), a stand-in with a real prototype (that mountain "Thunderbird Hotel" is Murodo on the Tateyama–Kurobe route), or pure fiction — and tells you accordingly, never bolting the prototype's facts onto the wrong building.
- **It digs where English can't.** The good details come from Japanese sources: a pharmacy that watched over one Shibuya corner for 62 years before closing on the last day of 2024; rockets that had to fight fishermen for the calendar, cleared to launch only ~190 days a year. Things you'd never surface by searching in English.
- **It rewards curiosity.** Every narration is both an 8-second hook and a several-minute deep dive. Blast past at 150 km/h and you catch the first line; cruise and you get a few; pull over and it tells you the whole thing. It's radio — the more you slow down, the more you learn.

Press **F6** to switch it on, **F11** to skip anything you're not in the mood for. That's the whole interface.

> Works on Windows 10/11 (64-bit) with Forza Horizon 5 or 6.

### Get it running (no build needed)

Grab the two archives from the latest [Release](../../releases):

| Archive | What's inside |
|---|---|
| `HorizonGuide-app-win-x64.zip` | The app (self-contained — no .NET install needed) |
| `HorizonGuide-data.zip` | Location data + narration audio |

Unzip the app anywhere, then drop the `data\` and `content\` folders from the data archive next to `HorizonGuide.exe`. Full install/launch steps live in the README inside the app archive (source copy: [`docs/RELEASE_README.md`](docs/RELEASE_README.md)).

Then, in-game: **Settings → HUD & Gameplay → Data Out**, set it **On**, IP `127.0.0.1`, port `5300`.

### Controls

| Hotkey (global — works while the game is focused) | Action |
|---|---|
| **F6** | Toggle roaming (narration only plays when it's on) |
| **F10** | Open / close the settings window |
| **F11** | Skip the current segment |

### Build from source

Requires the **.NET 9 SDK** on Windows.

```
git clone https://github.com/Namelessness5/horizon-tour-guide
cd horizon-tour-guide
dotnet build src/HorizonGuide.App
```

> This repository is **source only**. The narration data (audio + index) is distributed through Releases as `HorizonGuide-data.zip` — unzip its `content\` (and `data\`) next to the built executable, or into the repo root, and the app will find it. The content-generation pipeline that produces the narration is not part of this repository.

### How it works

- Receives Forza's **Data Out** UDP telemetry (`127.0.0.1:5300`, FH5/FH6 packet format auto-detected by length).
- Matches your position against hand-surveyed location polygons (`data/survey-drafts.json`).
- Estimates how long you'll stay (speed + distance to the boundary) and picks a narration segment sized to fit — see [`docs/PLAYBACK_DESIGN.md`](docs/PLAYBACK_DESIGN.md).
- Subtitles render on a transparent, always-on-top overlay, so OBS Display Capture records them together with the game.

### Surveying new locations

New places are marked with the included telemetry probe. Drive in-game and trace the location's trigger area by hand:

```
dotnet run --project tools/TelemetryProbe -- --survey
```

With the game in the foreground, use the hotkeys:

| Hotkey | Action |
|---|---|
| **F8** | record the center point (the thing being talked about) |
| **F9** | add a boundary point |
| **F7** | undo the last boundary point |
| **F10** | finish, then switch to the console to type a name |

Drive to the center and press **F8**, then drive **along the boundary** pressing **F9** at each corner (at least 3 points), and finish with **F10**. Switch back to the console window to name it — anything in parentheses becomes a private disambiguation note, not shown to players:

```
> DAIKOKU_PA Daikoku PA landmark
> HOTEL_THUNDERBIRD Thunderbird Hotel (fictional, modeled on Murodo on the Tateyama–Kurobe route) landmark
```

Then validate — reports area, self-intersection, and auto-infers parent/child nesting:

```
dotnet run --project tools/TelemetryProbe -- --check --write
```

Surveyed locations land in `data/survey-drafts.json`. Two rules worth knowing: press boundary points **in order** (the vertices *are* the polygon edges), and draw the polygon **generously** — it's a trigger area you enter a little early so there's time to talk, not a tracing of the object's actual size.

> **On the content pipeline.** Surveying a polygon is the one part of the content workflow included here. Turning a surveyed location into narration — gathering sourced facts, writing and localizing the script, and synthesizing the voice — runs through a separate generation pipeline that is **not (fully) released** in this repository. The finished narration is distributed as data through Releases.

### Project layout

```
src/
  HorizonGuide.Core       core logic — locations, content, scheduling (no UI, no platform deps)
  HorizonGuide.Forza      parses Forza Data Out telemetry
  HorizonGuide.Playback   audio playback (NAudio, in-process)
  HorizonGuide.App        WPF: transparent subtitle overlay + main loop + global hotkeys
tools/
  TelemetryProbe          telemetry probe / location surveying tool
  TelemetrySim            feeds fake telemetry so you can test without the game
  publish/pack.ps1        release packaging (app bundle + data bundle)
tests/                    unit tests
docs/                     design notes + release readme
```

### License

[MIT](LICENSE).

---

## 中文

你正开车穿过《Forza Horizon》里的日本。海浪里立着一座鸟居，霓虹路口迎面涌过，半山腰上孤零零挂着一家酒店。副驾上坐着个真懂行的人，探过身来，跟你讲这地方的来历。

这就是 Horizon Guide。它在游戏旁边安静地跑着，盯着你开到哪，一旦你拐进一个值得一说的地方，电台里就有个声音响起来，字幕在画面底部淡入。

它不只是一部"车轮上的维基"：

- **它知道这张地图是虚构的。** Forza 的日本经过压缩、重排、半虚构。Horizon Guide 会判断每个游戏地点在现实里*到底对应什么*——是照实复刻（涩谷十字路口、金阁寺）、是有真实原型的替身（半山那家"雷鸟酒店"其实是立山黑部路线上的室堂），还是纯属虚构——再据此讲给你听，绝不把原型的数字安到错的建筑上。
- **它挖英文挖不到的地方。** 真正的好细节来自日文源：一家药店守着涩谷那个街角 62 年，2024 年最后一天关门；火箭得跟渔民抢日历，一年最多只有约 190 天能发射。这些用英文怎么搜都搜不出来。
- **它奖励好奇心。** 每段解说既是 8 秒的钩子，也是几分钟的深讲。150 km/h 冲过去只听得到第一句；巡航能听到前几句；靠边停下，它就把整篇讲完。这是电台——你越慢下来，听到的越多。

按 **F6** 开，不想听按 **F11** 跳过。整个操作就这么简单。

> 支持 Windows 10/11（64 位），Forza Horizon 5 或 6。

### 直接用（不需要编译）

从最新 [Release](../../releases) 下两个压缩包：
或者[百度云盘](https://pan.baidu.com/s/1fGf5Qq27RgajjRdoxbCD8A?pwd=m3ic)

| 压缩包 | 内容 |
|---|---|
| `HorizonGuide-app-win-x64.zip` | 程序本体（自带运行时，无需装 .NET） |
| `HorizonGuide-data.zip` | 地点数据 + 语音音频 |

解压软件包到任意目录，再把数据包里的 `data\` 和 `content\` 两个文件夹放到 `HorizonGuide.exe` 同级目录。完整安装/启动步骤见软件包内的 README（源码里对应 [`docs/RELEASE_README.md`](docs/RELEASE_README.md)）。

然后在游戏里：**设置 → HUD 与游戏体验 → Data Out**，设为**开**，IP `127.0.0.1`，端口 `5300`。

### 操作

| 热键（全局，游戏在前台也管用） | 作用 |
|---|---|
| **F6** | 开 / 关漫游（开启后才会自动播报） |
| **F10** | 打开 / 关闭设置窗口 |
| **F11** | 跳过当前这段 |

### 从源码编译

需要 **.NET 9 SDK**（Windows）。

```
git clone https://github.com/Namelessness5/horizon-tour-guide
cd horizon-tour-guide
dotnet build src/HorizonGuide.App
```

> 本仓库**只含源码**。运行时需要的解说数据（音频 + 索引）通过 Releases 的 `HorizonGuide-data.zip` 分发——把它的 `content\`（和 `data\`）解压到可执行文件同级目录，或放到仓库根目录，程序会自动找到。产出解说的内容生成流程不包含在本仓库内。

### 它是怎么工作的

- 接收 Forza **Data Out** 的 UDP 遥测（`127.0.0.1:5300`，按包长自动识别 FH5/FH6 格式）。
- 用车辆坐标匹配预先勘定的地点多边形（`data/survey-drafts.json`）。
- 估算你**还能在这个地点待多久**（车速 + 到边界的距离），选一段长度合适的解说——设计细节见 [`docs/PLAYBACK_DESIGN.md`](docs/PLAYBACK_DESIGN.md)。
- 字幕渲染在透明置顶层上，可用 OBS 显示器采集连同游戏画面一起录制。

### 勘景：标注新地点

新地点用仓库自带的遥测探针来标。在游戏里开车，手动圈出这个地点的触发区：

```
dotnet run --project tools/TelemetryProbe -- --survey
```

游戏保持在前台，用热键采集：

| 热键 | 作用 |
|---|---|
| **F8** | 记录中心点（要讲的那个东西） |
| **F9** | 添加一个边界点 |
| **F7** | 撤销上一个边界点 |
| **F10** | 完成，然后切回控制台输名字 |

开到中心按 **F8**，再**沿着边界依次**按 **F9**（至少 3 个点），最后按 **F10**。切回控制台窗口给它命名——括号里的内容会存成不显示给玩家的消歧备注：

```
> DAIKOKU_PA 大黑停车场 landmark
> HOTEL_THUNDERBIRD 雷鸟酒店（虚构，原型是立山黑部路线上的室堂） landmark
```

然后校验——报告面积、自交，并自动推断地区套景观的嵌套关系：

```
dotnet run --project tools/TelemetryProbe -- --check --write
```

采完的地点落在 `data/survey-drafts.json`。两条要点：边界点必须**按顺序**打（顶点就是多边形的边）；多边形要画得**宽松**一点——它是"提前一点进入、好留出说话时间"的触发区，不是照着实物大小描边。

> **关于内容生成流程。** 勘景是内容工作流里唯一包含在本仓库中的一步。把一个勘定好的地点变成解说——联网取材（带来源）、写稿并本地化、再合成语音——走的是一套独立的生成流程，**没有（完全）放出**在本仓库里。成品解说以数据形式通过 Releases 分发。

### 代码结构

```
src/
  HorizonGuide.Core       核心逻辑——地点、内容、调度（无 UI、无平台依赖）
  HorizonGuide.Forza      解析 Forza Data Out 遥测
  HorizonGuide.Playback   音频播放（NAudio，进程内）
  HorizonGuide.App        WPF：透明字幕覆盖层 + 主循环 + 全局热键
tools/
  TelemetryProbe          遥测探针 / 勘景工具
  TelemetrySim            灌假遥测，不开游戏也能验
  publish/pack.ps1        打包发布（软件包 + 数据包）
tests/                    单元测试
docs/                     设计文档、发布说明
```

### 许可

[MIT](LICENSE)。
