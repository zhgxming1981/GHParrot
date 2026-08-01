# 工作摘要：WbClassification 重构与算法修正

## 项目路径
E:\程序\GH1 20260424\GH1

## 涉及文件
- `GH1\Component\Library\WbClassificationCore.cs` — 共享核心（约 2793 行）
- `GH1\Component\Library\WbClassificationMigration.cs` — 分类库迁移电池 + 测试套件（约 4808 行）
- `GH1\Component\Library\WbEntityClassifier.cs` — 实体分类电池（约 909 行）

## 已完成工作

### 1. 重构
原巨型文件 WbClassificationMigration.cs（7100 行）拆分为 3 个文件：
- WbClassificationCore.cs：WbUtil、WbFeature、WbHole、WbMigrationRecord、WbMigrationAudit、WbMigrationStore、WbClassificationSettings
- WbClassificationMigration.cs：WbMigrationCheckpoint、WbMirrorTest（含子测试类）、WbClassificationMigration:GH_Component、WbCreateMigrationDatabase:GH_Component、UI 类
- WbEntityClassifier.cs（新建）：实体分类电池

### 2. 算法 Bug 修复（已完成）
- 新增 `WbMigrationAudit.CanAutoMatch(a, b, enableDegenerateMatching)` — 退化孔与正常孔隔离：两侧都正常→通过；一侧退化一侧正常→拦截；两侧都退化→取决于开关
- `GeometryMatches` 入口调用 CanAutoMatch 做特征级隔离
- `HoleCompatible` 内部增加退化孔 vs 正常孔的拦截（单孔级防护，第 1596 行）
- WbEntityClassifier 分类循环：`matchCount > 1` 时 break 提前退出；镜像检测仅在 `matchCount == 0` 时执行
- 外轮廓面积容差下限从硬编码 1 改为 `fallbackTolerance²`

### 3. 帮助文本审查（已完成，未修改）
- 发现 5 项帮助文本与实际算法不匹配（圆形度未用、孔周长/面积名称暗示总和、法向 5% 放宽未提、退化倍数条件未区分、Length 开关可点击但始终启用）
- 14 项一致

## 待处理：编译错误

### WbClassificationCore.cs 缺少 3 个 using（拆分时遗漏）
```
using Rhino;            // RhinoDoc, RhinoMath
using System.Text;      // Encoding.UTF8
using GH_IO.Serialization; // GH_IWriter, GH_IReader (在 WbClassificationSettings.Write/Read 中使用)
```

### WbClassificationMigration.cs 第 2202 行
`GeometryMatches(...)` 缺少 `WbMigrationAudit.` 前缀（编译被 Core 错误掩盖）

## 待实施算法修改（基于 6 件误判分析）

### 根因确认
6 件实际相同的零件被拆为 6 类，核心原因：
1. **主因**：11 个退化孔导致零件被退化孔隔离策略排除，根本不参与自动聚类
2. **次因**：外轮廓计数不稳定（49/50），即使参与也会被硬拦截

### 修改一：开关语义调整（用户已确认）
- 原：开关控制"是否参与退化匹配"
- 新：开关控制"退化孔容差是否放宽"
- 退化孔件始终走聚类管线，`CanAutoMatch` 守卫确保不与正常孔件交叉
- 开关关闭时：退化孔件互相比较，使用正常容差
- 开关开启时：退化孔件互相比较，容差放大 ×degenMult
- UI 文本从"退化孔低置信度匹配"改为"退化孔容差放宽"

### 修改二：移除 OuterProfilesMatch 硬否决
- GeometryMatches 中不再调用 OuterProfilesMatch
- 外轮廓数据保留存储（序列化/反序列化不动）
- 提取代码不动，WbOuterProfile 增加 FaceIndex 字段用于诊断
- 日志加 Remark："外轮廓匹配已关闭"

### 修改三：日志语义修正
- "实际不同、编号相同" → 仅用于经过完整匹配流程后冲突的场景
- 退化孔未参与聚类 → "含退化孔未自动判定同件关系，独立入库待复核"

### 修改四：诊断增强
- WbOuterProfile 加 FaceIndex
- WbFeature.DiagnosticLines() 输出外轮廓明细（面索引、面积、周长、法向）

## 构建命令
```
& 'd:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' 'E:\程序\GH1 20260424\GH1\parrot.sln' /p:Configuration=Debug /p:Platform=x64 /t:Build /v:minimal
```

.NET SDK 10.0.302，VS 2026 Community at d:\Program Files\Microsoft Visual Studio\18\
外部 SDK 路径可用：Rhino 8、Tekla 2025、AutoCAD 2020、Siemens NX 2512 均在 D:\Program Files
