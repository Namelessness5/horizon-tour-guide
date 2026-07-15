# Horizon Guide —— Forza Horizon 车载 AI 导览电台

开车路过一个地方，副驾上有个懂行的朋友随口讲两句。

程序在游戏外面独立运行，读 Forza Horizon 的遥测判断你开到了哪，然后播一段预先生成好的当地解说，并在画面底部打字幕。按 **F6** 开关，不想听按 **F11** 跳过。

> 支持 Forza Horizon 5 / 6，Windows 10/11 64 位。

---

## 直接用（不需要编译）

从 [Releases](../../releases) 下最新版的两个压缩包：

| 压缩包 | 内容 |
|---|---|
| `HorizonGuide-app-win-x64.zip` | 程序本体（自带运行时，无需装 .NET） |
| `HorizonGuide-data.zip` | 地点数据 + 语音音频 |

解压后把数据包里的 `data\` 和 `content\` 两个文件夹放到 `HorizonGuide.exe` 同一目录即可。完整安装/启动步骤见软件包内的 `README.md`（源码里对应 [`docs/RELEASE_README.md`](docs/RELEASE_README.md)）。

---

## 从源码编译

需要 **.NET 9 SDK**（Windows）。

```
git clone <this-repo>
cd horizon_tour_guide
dotnet build src/HorizonGuide.App
```

产物在 `src\HorizonGuide.App\bin\Debug\net9.0-windows\HorizonGuide.exe`。

> **本仓库只含源码，不含内容数据。** 运行时需要的地点音频和索引通过 Releases 的
> `HorizonGuide-data.zip` 分发——把它的 `content\`（和 `data\`）解压到可执行文件同级目录，
> 或放到仓库根目录，程序会自动向上查找。
>
> 解说内容由一套内部生成流程产出，该流程不包含在本仓库内。

---

## 游戏侧设置（一次性）

程序靠 Forza 的 **Data Out**（遥测输出）知道你在哪。游戏里打开：

**设置 → HUD 与游戏体验 → Data Out**

| 选项 | 值 |
|---|---|
| Data Out | 开 |
| IP | `127.0.0.1` |
| 端口 | `5300` |

---

## 操作

| 热键（全局，游戏在前台也管用） | 作用 |
|---|---|
| **F6** | 开 / 关漫游（开启后才会自动播报） |
| **F10** | 打开 / 关闭设置窗口 |
| **F11** | 跳过当前这段 |

开进有内容的地点，字幕和语音就来了。

---

## 它是怎么工作的

- 接收 Forza **Data Out** 的 UDP 遥测（`127.0.0.1:5300`，按包长自动识别 FH5/FH6 格式）。
- 用车辆坐标匹配预先勘定的地点多边形（`data/survey-drafts.json`）。
- 根据你**还能在这个地点待多久**（车速 + 到边界的距离）选一段长度合适的解说：
  高速冲过去只来得及讲第一句，停下来能整篇听完。设计细节见 [`docs/PLAYBACK_DESIGN.md`](docs/PLAYBACK_DESIGN.md)。
- 字幕是覆盖在游戏画面上的透明置顶层，可用 OBS 显示器采集连同游戏画面一起录制。

---

## 代码结构

```
src/
  HorizonGuide.Core       地点、内容、调度等核心逻辑（无 UI、无平台依赖）
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

---

## 许可

TODO：待补充 License。
