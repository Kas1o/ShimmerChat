# 数据存储说明

ShimmerChat 的持久化数据全部位于应用目录下的 `Data/`。KV 数据、聊天消息、调试输出、向量库各自独立选择后端（LiteDB 或文件），互不影响。

---

## 目录结构

```
Data/
├── storage_settings.json        # 存储配置（唯一事实来源）
├── .layout_migration            # 旧目录 → Data/ 的布局迁移完成标记
├── .migration_marker            # 各仓库存储后端迁移记录
├── LiteDB/
│   ├── kvdata.db                # KV 数据（Agent、Chat、设置、记忆文本…）
│   ├── chat_message.db          # 聊天消息
│   ├── debug_output.db          # 调试输出
│   ├── vector.db                # 向量（单文件，所有集合）
│   └── memory.db                # 记忆真值库（不随向量后端变化）
├── LocalFileStorage/
│   ├── kvdata/                  # KV 文件后端：<space>/<key>.json
│   ├── chat_message/            # 消息文件后端：<chatGuid>/<messageGuid>.json
│   ├── debug_output/            # 调试输出文件后端：<entryId>.json
│   └── vector/                  # 向量文件后端：每个集合一个 JSON 文件
└── UserUploadImage/             # 用户上传 / 生成图像（以 /userimages 暴露）
```

- `Data:RootPath`（`appsettings.json`）可以把整个 `Data/` 换到别处；绝对路径原样使用，相对路径按应用目录解析。
- 各仓库的 `LocalFileStorage/<store>/` 目录在该仓库选择文件后端、执行迁移或首次启动做迁移检测时创建。
- 图像引用只保存文件名，因此迁移目录不会破坏历史消息。

---

## storage_settings.json

文件不存在时，程序用内置默认值生成；已存在时只读取、不覆盖。文件缺失必填节、JSON 非法、存储类型非法都会**直接中止启动**并给出文件路径，不做静默兜底。

```json
{
  "SchemaVersion": 1,
  "KVData": {
    "StorageType": "LiteDB",
    "AutoMigrateOnStartup": true,
    "AutoMigrateFrom": "LocalFileStorage",
    "ClearSourceAfterMigration": false,
    "ForceMigration": false
  },
  "ChatMessage": {
    "StorageType": "LiteDB",
    "AutoMigrateOnStartup": true,
    "AutoMigrateFrom": "LocalFileStorage",
    "ClearSourceAfterMigration": false,
    "ForceMigration": false
  },
  "DebugOutput": {
    "StorageType": "LiteDB",
    "AutoMigrateOnStartup": true,
    "AutoMigrateFrom": "LocalFileStorage",
    "ClearSourceAfterMigration": false,
    "ForceMigration": false
  },
  "VectorStore": {
    "Provider": "LiteDb"
  }
}
```

### 每个仓库的配置项

| 配置项 | 说明 | 默认值 | 可选值 |
|--------|------|--------|--------|
| `StorageType` | 该仓库使用的后端 | `"LiteDB"` | `"LiteDB"` / `"LocalFileStorage"` |
| `AutoMigrateOnStartup` | 启动时自动迁移该仓库 | `true` | `true` / `false` |
| `AutoMigrateFrom` | 迁移源后端；为空表示不自动迁移 | `"LocalFileStorage"` | `"LiteDB"` / `"LocalFileStorage"` / `null` |
| `ClearSourceAfterMigration` | 迁移成功后清空源仓库 | `false` | `true` / `false` |
| `ForceMigration` | 即使已有迁移标记也重新迁移（需 CLI 确认） | `false` | `true` / `false` |

`KVData`、`ChatMessage`、`DebugOutput` 三节的键完全相同，可以分别设置。例如只把调试输出放到文件、其余继续用 LiteDB：

```json
{
  "KVData":       { "StorageType": "LiteDB",        "AutoMigrateOnStartup": true,  "AutoMigrateFrom": "LocalFileStorage" },
  "ChatMessage":  { "StorageType": "LiteDB",        "AutoMigrateOnStartup": true,  "AutoMigrateFrom": "LocalFileStorage" },
  "DebugOutput":  { "StorageType": "LocalFileStorage", "AutoMigrateOnStartup": true, "AutoMigrateFrom": "LiteDB" },
  "VectorStore":  { "Provider": "LiteDb" }
}
```

### 向量后端

| `Provider` | 数据位置 | 说明 |
|------------|----------|------|
| `LiteDb`（默认） | `Data/LiteDB/vector.db` | 单文件，所有集合共用，适合大数据量 |
| `File` | `Data/LocalFileStorage/vector/` | 每个集合一个 JSON 文件，可直接查看 / 备份，写入为整文件替换 |
| `InMemory` | 不落盘 | 进程退出即丢失，仅用于测试与轻量场景 |

记忆文本（`memory.db`）始终落盘，与向量后端无关；删掉向量数据仍可从文本重建索引。

---

## 迁移

### 1. 旧目录 → `Data/`（一次性布局迁移）

升级到新版本后首次启动会自动执行，旧目录**只复制、不删除**：

| 旧位置 | 新位置 |
|--------|--------|
| `LiteDBKVData/data.db#kvdata` | `Data/LiteDB/kvdata.db` |
| `LiteDBKVData/data.db#messages` | `Data/LiteDB/chat_message.db` |
| `LiteDBKVData/data.db#debug_output` | `Data/LiteDB/debug_output.db` |
| `VectorStore/*.db`（按 owner 分文件） | `Data/LiteDB/vector.db`（合并为单文件） |
| `Memory/records.db` | `Data/LiteDB/memory.db` |
| `KVData/` | `Data/LocalFileStorage/kvdata/` |
| `ChatMessages/` | `Data/LocalFileStorage/chat_message/` |
| `DebugOutput/` | `Data/LocalFileStorage/debug_output/` |
| `UserUploadImage/` | `Data/UserUploadImage/` |
| `KVData/.migration_marker` | `Data/.migration_marker` |

- 完成标记：`Data/.layout_migration`。存在且 `LayoutVersion >= 2` 时跳过。
- 幂等：文件按“目标不存在才复制”，LiteDB 记录按 `_id` upsert，重复执行不会产生重复数据。
- 迁移失败会中止启动（不写完成标记），修复后下次启动继续。
- 向量合并后会把 `memory.db` 中记录的索引身份改写为新 `vector.db` 位置，升级后无需因路径变化强制重建索引。
- 需要重新执行时删除 `Data/.layout_migration`；旧目录确认无误后可自行清理。

升级用户的旧配置兼容：

- `appsettings.json` 里的 `KVDataStorage` / `VectorStore` 只在 `storage_settings.json` **不存在**时用于生成初始配置；生成后运行期以 `storage_settings.json` 为准。
- 如果升级时 appsettings 已被覆盖、`storage_settings.json` 尚不存在：按旧数据实际位置**逐仓库推断**（旧 LiteDB 有数据 → `LiteDB`；旧 LiteDB 为空但旧文件目录有数据 → `LocalFileStorage`），推断结果会打印在启动日志里，可在 `storage_settings.json` 中改回。
- **同一仓库两边都有数据时不会猜测或自动迁移**：启动直接报错并中止，要求先显式指定该仓库的 `StorageType`（改 `appsettings.json` 的 `KVDataStorage`，或手工创建 `storage_settings.json`）。这样不会因选错后端覆盖或“看不见”另一份数据。
- 旧配置为 `LiteDB` 时，布局迁移会自动补齐 `KVData` / `ChatMessage` / `DebugOutput` 的迁移标记，避免启动时又拿旧文件存储覆盖 LiteDB 中的新数据。

### 2. 同一仓库切换后端（按仓库独立）

每个仓库按自己的 `AutoMigrateFrom → StorageType` 执行，迁移记录写在 `Data/.migration_marker`（按仓库隔离）：

```json
{
  "Version": 2,
  "Migrations": [
    { "Store": "KVData", "SourceStorage": "LocalFileStorage", "TargetStorage": "LiteDB",
      "MigratedCount": 150, "SourceCleared": false, "MigrationTime": "2026-11-25T00:00:00Z" }
  ]
}
```

- 同仓库同方向已迁移过时跳过；需要重跑请设 `ForceMigration: true`，启动时在 CLI 输入 `yes` 确认。
- 消息与调试输出的迁移会先清空目标仓库再按原 Id 导入，重复执行不会产生重复条目。
- 迁移失败会中止启动且不写标记，下次启动自动重试。

### 3. 从旧版迁移标记读取

旧版单对象标记（`KVData/.migration_marker`）仍会被读取，仅映射为 `KVData` 仓库的记录；新写入一律落到 `Data/.migration_marker`。

### 4. 向量后端切换

`LiteDb` / `File` / `InMemory` 之间不会自动搬运向量数据。切换后 `IVectorStoreService.StoreIdentity` 变化，
记忆索引会被判定为未就绪；在设置页重建索引即可，记忆文本（`memory.db`）不受影响。

---

## 建议

- **备份**：直接复制整个 `Data/` 即可；LiteDB 后端请连同 `*-log.db` 一起复制。
- **生产环境**：KV / 消息 / 调试输出推荐 LiteDB；向量按规模在 `LiteDb` 与 `File` 之间选择。
- **开发调试**：把对应仓库切到 `LocalFileStorage` 可直接查看 / 修改 JSON。
- **不要**在运行中手动移动或删除 `Data/` 下的数据库文件；先停止应用。
