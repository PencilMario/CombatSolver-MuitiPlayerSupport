# 问题包与提交协议 v2

0.33.8 开始，玩家描述、联系信息和自动元数据分开提交。旧问题包仍可由 CheckpointTool 读取；新客户端向 miaovps 的 `/api/v2/reports` 上传，旧 `/api/v1/reports` 接口保留。

## 包目录

```text
report.json                 报告身份、版本、玩家描述、战斗元数据、自动分类、战损比较
README.txt
diagnostics/                环境、设置、当前状态、路线、重算审计、日志
replay/manifest.json
replay/checkpoint.json      原 v2 检查点索引，包含所有恢复材料路径
replay/current/...          当前战斗；战后导出改为 recent，仅保留一场
```

报告编号在导出时生成。文件名含 Mod 版本、遭遇 ID 和报告编号。原生事件、RNG、检查点选择及最多 6 个检查点的保留逻辑沿用原协议。

## 元数据

- `schemaVersion=2`、`reportId`（32 位小写 GUID）、`createdAt`（UTC）、`modVersion`（三段版本）、`gameVersion`（游戏发布版本；无法获取为 null）。
- `playerDescription` 只包含玩家文字，最多 4000 字符。联系方式只在表单 `contact` 中传输，最多 64 字符；不写入问题包设置或 report.json。
- `combat`：sessionId、encounterId/encounterName/encounterType、characterId/characterName、monsters（去重的 id/name）、ascension、act（从 1 开始）、floor、controlMode。主线程随取证冻结，怪物集合保留这场战斗已观察到的种类；战后导出使用这场战斗的缓存。没有战斗为 null。
- `classification`：状态不一致、执行漂移、续接缺失、计划耗尽、手操偏离的重算次数，以及带 kind/count/detail 的异常列表。计划外重算 = 状态不一致 + 执行漂移。
- `manualProjectionComparison` 保留比较前后检查点身份、回合和预测总战损。`hpLoss.kind=manual_projection`，before/after 为这次比较的预测总战损；`reduction=before-after`，正数下降、负数上升、0 相等。它是最近一次手操前后预测比较，不是实测战损或最大历史改善；无比较时 hpLoss 为 null。
- `bundle` 声明包结构版本及 `checkpointIndexPath=replay/checkpoint.json`。元数据筛选与恢复成功是不同概念，是否能恢复须由回放工具验证。

## 上传与后端

multipart 字段：`report`（ZIP）、`metadata`（与包内 report.json 内容一致）、`submissionId`（等于 reportId）、`description`（等于 playerDescription）、`contact`。沿用 X-CombatSolver-Key，ZIP 上限 128 MiB，元数据上限 256 KiB。服务端检查 ZIP 路径、重复项、展开总量和元数据一致性；不解压玩家文件到应用目录。

新提交返回 201；相同报告 ID、文件内容、元数据及联系方式重试返回 200 和同一回执；编号被不同内容占用返回 409。回执含 id、submissionId、sizeBytes、receivedAt、storedAs、duplicate。客户端只在确认编号和实收大小后显示成功，失败保留本地包。

HTTPS 地址为 `https://43.249.195.13:10289/api/v2/reports`。客户端固定证书 SHA-256，同时校验名称和有效期；不继承游戏代理、不接受重定向。

后台按 Mod/游戏版本、战斗、怪物、角色、战斗类型、操作方式、异常类型、战损下降范围、楼层范围、进阶和计划外重算次数组合筛选，可按时间、战损下降或重算次数排序。未知值与零分开；数值筛选排除未知值。列表、分页、批量下载和批量删除共用一个筛选契约。

汇总 ZIP 包含 `index.json`（报告清单、元数据、文件可用性）、`反馈汇总.csv` 和 `reports/<reportId>.zip`。被清理的文件在清单中显式标为不可用，元数据仍保留。后端迁移旧数据库只新增可空列与索引，不臆测旧报告缺失字段。
