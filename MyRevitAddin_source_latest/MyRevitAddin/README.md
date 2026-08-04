# MyRevitAddin（兮的 Revit 插件集）

面向 **Revit 2018 / 2020** 的 .NET Framework 4.8 插件集，功能覆盖：**机电工具 / 房间工具 / 族工具 / 门窗工具 / 楼板工具 / 柱工具 / 标记工具 / 图纸工具 / 视图工具 / 标高工具 / 分析可视化 / AI 助手** 等 14 个面板共 30+ 按钮。

> 功能开发以「深圳地区项目报建流程」为核心场景：批量族命名规则、构件标识填充、模型按系统拆分、房间与标高归整等都是根据实际项目经验沉淀的定制功能。

---

## 1. 功能总览（选项卡「我的工具」）

### 1.1 标记工具
| 按钮 | 功能 |
|---|---|
| 关键标记\n避障 | 批量选中关键标记，自动检测重叠并平移避让 |

### 1.2 图纸工具
| 按钮 | 功能 |
|---|---|
| 批量复制\n图纸 | 批量复制图纸与视图，支持视图替换与自定义命名 |

### 1.3 视图工具
| 按钮 | 功能 |
|---|---|
| 批量复制\n视图样板 | 批量复制视图并设置视图样板，命名格式：`{原名}（{样板名}）` |
| 添加过滤器\n到样板 | 批量添加过滤器到视图样板并关闭可见性 |

### 1.4 AI 助手
| 按钮 / 面板 | 功能 |
|---|---|
| AI 助手 | 自然语言控制 Revit（基于千问 API，Revit 启动后常驻「AI 助手」可停靠面板） |

### 1.5 标高工具
| 按钮 | 功能 |
|---|---|
| 重新归属\n标高 | 选择两个标高，将范围内所有构件重新归属到下部标高 |

### 1.6 族工具（核心面板一）
| 按钮 | 功能 |
|---|---|
| 内建族转\n可载入族 | 选择内建族，导出几何/参数为可在其他项目复用的 `.rfa` 常规族 |
| 批量族\n重命名 | 加前后缀、查找替换、插入/截断、序号编号（深圳报建预设），预览确认后一次性改族名/族类型名 |
| 批量改\n参数 | 筛选类别→选源参数→选目标参数→预览→一键复制（支持类型/实例参数互拷 / 处理选中构件或全模型两种模式） |
| 批量建\n项目参数 | 选类别+设参数属性+批量输入名称，一键创建多个**文本型**实例参数并绑定到类别；同名参数自动追加已有类别绑定 |
| 批量修改\n构件属性 | 衍生自「房间名→属性参数」：选任意类别、勾参数、填固定值，一键批量写入多个属性（支持实例/类型参数同时修改；只写勾选行；类型参数自动去重；空值二次确认） |

### 1.7 机电工具（核心面板二）
| 按钮 | 功能 |
|---|---|
| 按系统\n选中构件 | 按机电系统类型（管道/风管）筛选并选中构件，支持批量附加属性 |
| 表格\n导入回导 | 导出构件**类型参数**到 CSV → Excel 填写 → 回导入 Revit 更新属性 |
| 按系统\n拆分模型 | 复制当前模型 3 份，分别删除非风管/非桥架/非水管系统构件，生成 3 个独立 `.rvt` 文件；建筑构件、标高、轴网、视图、图纸、明细表、标注等三份共享内容均保留 |
| 管件\n深圳标识 | 按管件类型名填充参数「深圳构件标识」：**【优先】** 含「变径接头/活接头/三通/弯头/四通/接头」→ 填活接头/对应关键字；**【次等】** 仅含「大小头/变径」且不含其他关键字 → 填「过渡件」。管道管件/风管管件/桥架配件/线管配件四类可勾选，预览表含缺参数红标与已有值提示；仅处理选中或全模型两种模式 |

### 1.8 房间工具（核心面板三）
| 按钮 | 功能 |
|---|---|
| 生成房间\n分割线 | 选择楼层，按指定间距自动生成房间分割线网格 |
| 房间缺口\n检测 | 检测房间边界是否闭合，用红色十字标记缺口位置 |
| 批量修改\n房间名称 | 按规则批量改房名：前后缀、查找替换（支持正则）、截取、自动编号；预览后一键应用（**不自动去重**，允许重名房间） |
| 房间名→\n属性参数 | 把房间名称批量赋值到所有可写属性参数（自动识别实例/类型参数，可多选） |
| 重叠房间\n检测 | 一键查询项目中完全重叠房间与空白待删房间，Tab 页分别展示；支持成对单独选中（窗口非模态，可同时操作 Revit） |

### 1.9 门窗工具
| 按钮 | 功能 |
|---|---|
| 门窗\n开启面积 | 读取门窗长度/高度参数，计算 `长×高/1000000` 写入指定开启面积实例参数（自动做 `Parameter.SetValueString` 显示值单位换算） |

### 1.10 楼板工具
| 按钮 | 功能 |
|---|---|
| 楼板\n长宽计算 | 通过楼板面积+周长推算矩形长宽，写入「长度」「宽度」实例参数；可自由选择源/目标参数 |

### 1.11 柱工具
| 按钮 | 功能 |
|---|---|
| 异形柱\n长宽计算 | 通过体积+高度推算正方形边长，写入「长度」「宽度」实例参数；可自由选择源/目标参数 |

### 1.12 分析可视化
| 按钮 | 功能 |
|---|---|
| 净高\n图例着色 | 按净高参数批量对当前视图填充区域着色：越高越浅、越低越深 |

### 1.13 测试
| 按钮 | 功能 |
|---|---|
| Hello\nRevit | 最小化冒烟测试命令，确认插件整体加载正常 |

---

## 2. 开发 / 编译要求

| 项 | 要求 |
|---|---|
| 目标框架 | **.NET Framework 4.8**（net48） |
| Revit 版本 | **2018 / 2020**（两个 csproj 分别引用各自 RevitAPI/RevitAPIUI） |
| 构建工具 | Visual Studio 2022 / MSBuild 17+（本仓库自带 `find_msbuild.ps1` 自动查找 VS 2022 的 MSBuild） |
| 构建输出目录 | `bin\Debug\`（Revit 2020 配置） / `bin\Debug_2018\`（Revit 2018 配置） |

### 关键项目文件

| 文件 | 作用 |
|---|---|
| [MyRevitAddin.slnx](MyRevitAddin.slnx) | Visual Studio 新式解决方案文件 |
| [MyRevitAddin.csproj](MyRevitAddin.csproj) | 主工程（源码） |
| [MyRevitAddin_2020.csproj](MyRevitAddin_2020.csproj) | **Revit 2020** 配置（包含 Revit 2020 的 DLL 引用） |
| [MyRevitAddin_2018.csproj](MyRevitAddin_2018.csproj) | **Revit 2018** 配置 |
| [MyRevitAddin.addin](MyRevitAddin.addin) | Revit 插件清单文件（部署时复制到 Addins 目录） |
| [App.cs](App.cs) | Ribbon 注册入口（14 个面板、30+ 按钮注册代码） |

### 编译命令（PowerShell，仓库根目录）

```powershell
# 自动探测 MSBuild + 编译 Revit 2020 版
./build_2020.ps1

# 编译 Revit 2018 版
./build_2018.ps1

# 同时编译两个版本
./build_all.ps1
```

或直接调用 MSBuild：

```powershell
& "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
    MyRevitAddin_2020.csproj /p:Configuration=Debug /p:Platform=AnyCPU
```

---

## 3. 安装 / 部署

### 方式 A：使用仓库自带部署脚本（推荐，开发机上）

**先完全关闭 Revit**（否则 DLL 被占用无法覆盖）：

```powershell
# 一键编译 2020 + 复制到用户 Addins 2020 目录
./_deploy_now.cmd
```

脚本内容：自动用 `find_msbuild.ps1` 定位 MSBuild → 编译 → 复制 `MyRevitAddin.dll / .pdb / .addin` 到  
`%APPDATA%\Autodesk\Revit\Addins\2020\`。

### 方式 B：下载 Release 二进制包（给最终用户）

下载 `MyRevitAddin_release_2020_vX.Y.Z.zip`，解压后：

1. 关闭所有 Revit
2. 双击 `Install_For_AllUsers.bat` 或手动：
   - 把 `MyRevitAddin.dll / .pdb / MyRevitAddin.addin` 三个文件复制到：
   - **当前用户**：`%APPDATA%\Autodesk\Revit\Addins\2020\`
   - **所有用户**：`%PROGRAMDATA%\Autodesk\Revit\Addins\2020\`
3. 打开 Revit 2020 → 任意项目 → 功能区出现「我的工具」选项卡即安装成功

### 卸载

关闭 Revit → 从 `%APPDATA%\Autodesk\Revit\Addins\2020\` 删除三个文件即可。

---

## 4. 设计与编码约定（贡献者参考）

以下是本项目稳定运行的硬约束，新增插件请遵守：

1. **线程规则**：所有 Revit COM 对象访问（`Element.get_Parameter`、`Document.GetElement` 等）必须在 UI 线程，不可在 Task/BackgroundWorker 内跨线程直接读。跨 UI 操作一律使用 `Dispatcher.Invoke`。
2. **参数写入**：数值型参数一律先 `p.SetValueString(displayValue)`（按项目显示单位解释），失败才回退 `p.Set(internalValueInFeet)`，避免单位搞错。文本/整数/YesNo 类型按 StorageType 精确转换。
3. **DataGridView 复选框列**：必须 `ReadOnly = false`（DataGridView 级不是列级），并注册 `CurrentCellDirtyStateChanged → CommitEdit` 保证勾选立即生效。
4. **SplitContainer 规避**：优先用**纯 Dock 面板组合**或 TableLayoutPanel 做左右/上下布局。SplitContainer 在 `Dock=Fill + MinSize 较大` 时会在构造流程内部触发 `SplitterDistance` 越界 `InvalidOperationException`，且无法用 try/catch 覆盖。
5. **参数名查找**：一律不区分大小写（`StringComparison.OrdinalIgnoreCase`），找不到实例参数就到 `ElementType` 上再找一次。
6. **批量操作容错**：批量赋值不要整体一个事务一条失败就全回滚；推荐分组（500 条/事务）或单条事务，末尾统一汇总成功 X / 失败 Y（明细写日志文件）。
7. **日志路径**：`%LOCALAPPDATA%\MyRevitAddin_Logs\`，文件名按命令+日期命名，便于用户上传排错。

---

## 5. 目录结构（源码）

```
MyRevitAddin/
├── .gitignore                  # 排除 bin/obj/deploy/data/历史对话 等
├── README.md                   # 本文件
├── MyRevitAddin.slnx           # VS 2022 新式解决方案
├── MyRevitAddin.csproj         # 主工程
├── MyRevitAddin_2020.csproj    # Revit 2020 引用配置
├── MyRevitAddin_2018.csproj    # Revit 2018 引用配置
├── MyRevitAddin.addin          # Revit Addins 清单文件
├── App.cs                      # Ribbon 入口（注册 14 面板+所有按钮）
├── GlobalUsings.cs             # 全局 using （RevitAPI/RevitAPIUI 等）
├── Utils.cs                    # 通用工具（参数查找/写入/单位换算等）
├── Class1.cs                   # Hello Revit 冒烟测试命令
├── build_*.ps1 / build.ps1     # 编译脚本
├── deploy.ps1 / _deploy_now.cmd / deploy_now.cmd / 一键部署_2020.cmd  # 部署脚本
├── find_msbuild.ps1            # 自动查找 MSBuild
├── check_revit.ps1             # 检测 Revit 进程占用（部署前置校验）
├── ── 各插件代码按目录分组（每插件 Command + Dialog 两个文件） ──
├── AIAssistant/                # AI 助手面板 + 千问 API + Revit 操作执行器
├── BatchCreateSharedParams/    # 批量建项目参数
├── BatchEditElementParams/     # 批量修改构件属性
├── BatchFillFamilyParameters/  # 批量填族参数
├── BatchParamCopy/             # 批量参数互拷
├── BatchRenameFamilies/        # 批量族重命名
├── BatchRenameRooms/           # 批量改房间名
├── ColumnDimensionCalculator/  # 异形柱长宽计算
├── DoorWindowOpeningArea/      # 门窗开启面积
├── ExcelImportExport/          # 表格导入回导（类型参数）
├── FloorDimensionCalculator/   # 楼板长宽计算
├── InPlaceFamilyConverter/     # 内建族转可载入族
├── NetHeightColorizer/         # 净高着色
├── PipeFittingSzMarker/        # 管件深圳构件标识
├── RoomCopyNameToId/           # 房间名→属性参数
├── RoomGapDetector/            # 房间缺口检测
├── RoomOverlapChecker/         # 重叠房间检测
├── RoomSeparation/             # 房间分割线
├── SelectBySystem/             # 按系统选中构件
├── SplitByMepSystem/           # 按系统拆分模型
└── Properties/AssemblyInfo.cs
```

---

## 6. 许可证

本项目代码用于个人/内部项目开发。依赖的 **RevitAPI.dll / RevitAPIUI.dll** 版权归 Autodesk 所有，本仓库不打包这两个 DLL，编译时请从本机 Revit 安装目录的 `Revit 2020/` 或 `Revit 2018/` 目录中按需引用。
