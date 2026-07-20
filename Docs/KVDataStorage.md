# KV 数据存储说明

ShimmerChat 支持两种 KV（键值对）数据存储方式：**LiteDB**（默认）和 **LocalFileStorage**（传统文件存储）。

---

## LiteDB 优势

LiteDB 是默认的存储方案，相比传统文件存储具有以下优势：

### 1. 性能优势
- **更快的读写速度**：使用内存缓存和索引，避免频繁的文件 I/O 操作
- **批量操作支持**：支持事务性批量写入，大幅提升大数据量导入性能
- **查询优化**：内置索引机制，数据查询更高效

### 2. 数据一致性
- **事务支持**：支持 ACID 事务，确保数据操作的原子性
- **单文件存储**：所有数据存储在一个 `.db` 文件中，便于管理和备份
- **自动恢复**：数据库损坏时可自动恢复

### 3. 功能特性
- **空间隔离**：原生支持按 SpaceId 分区存储
- **唯一约束**：支持 SpaceId + Key 的唯一性约束
- **数据类型支持**：支持复杂数据类型的序列化存储

### 4. 运维便利
- **备份简单**：只需复制单个数据库文件
- **跨平台**：数据库文件可在不同操作系统间迁移
- **零配置**：无需额外配置，开箱即用

---

## 目录结构

```
AppBaseDirectory/
├── KVData/                    # 数据目录（含 LocalFileStorage 数据和迁移标记）
│   ├── .migration_marker      # 迁移标记文件（记录迁移历史，始终在此目录下）
│   ├── Space1/                # 空间数据文件夹
│   │   ├── key1.json
│   │   └── key2.json
│   └── Space2/
│       └── ...
└── LiteDBKVData/              # LiteDB 数据目录
    └── data.db                # LiteDB 数据库文件
```

---

## 配置说明

### 完整配置示例

编辑 `appsettings.json`：

```json
{
  "KVDataStorage": {
    "StorageType": "LiteDB",
    "AutoMigrateOnStartup": true,
    "AutoMigrateFrom": "LocalFileStorage",
    "ClearSourceAfterMigration": false,
    "ForceMigration": false
  }
}
```

### 配置项说明

| 配置项 | 说明 | 默认值 | 可选值 |
|--------|------|--------|--------|
| `StorageType` | 存储类型 | `"LiteDB"` | `"LiteDB"` / `"LocalFileStorage"` |
| `AutoMigrateOnStartup` | 启动时自动迁移 | `true` | `true` / `false` |
| `AutoMigrateFrom` | 迁移源存储类型 | `"LocalFileStorage"` | `"LiteDB"` / `"LocalFileStorage"` / `null` |
| `ClearSourceAfterMigration` | 迁移后清空源数据 | `false` | `true` / `false` |
| `ForceMigration` | 强制重新迁移（需 CLI 确认） | `false` | `true` / `false` |

---

## 数据迁移

### 自动迁移（首次）

默认配置下，应用启动时会自动检测并执行迁移：
- 从 `LocalFileStorage` 迁移到 `LiteDB`
- 迁移完成后在 `KVData/.migration_marker` 创建标记文件
- 下次启动时检测到标记会自动跳过迁移

### 迁移标记

迁移标记文件记录了迁移历史信息：

```json
{
  "MigrationTime": "2026-05-10T05:30:00Z",
  "SourceStorage": "LocalFileStorage",
  "TargetStorage": "LiteDB",
  "MigratedCount": 150,
  "SourceCleared": false,
  "Version": 1
}
```

### 强制重新迁移

如需重新执行迁移（例如数据损坏或需要同步新数据）：

1. 修改配置启用强制迁移：
```json
{
  "KVDataStorage": {
    "ForceMigration": true
  }
}
```

2. 启动应用，CLI 会提示确认（提示文字中 `{migrateFrom}` 和 `{targetType}` 为动态变量，取决于配置）：
```
⚠️  WARNING: Force migration is enabled!
This will re-migrate all data from {migrateFrom} to {targetType}.
Existing data in target storage may be duplicated.
Do you want to continue? (yes/no):
```

3. 输入 `yes` 确认后执行迁移，输入其他内容则取消

4. 迁移完成后建议将 `ForceMigration` 改回 `false`

### 反向迁移（LiteDB → LocalFileStorage）

如需切换回文件存储：

```json
{
  "KVDataStorage": {
    "StorageType": "LocalFileStorage",
    "AutoMigrateOnStartup": true,
    "AutoMigrateFrom": "LiteDB",
    "ClearSourceAfterMigration": false,
    "ForceMigration": false
  }
}
```

---

## 使用传统文件存储

如需完全禁用 LiteDB，使用传统的 LocalFileStorage：

```json
{
  "KVDataStorage": {
    "StorageType": "LocalFileStorage",
    "AutoMigrateOnStartup": false,
    "AutoMigrateFrom": null,
    "ClearSourceAfterMigration": false,
    "ForceMigration": false
  }
}
```

---

## 存储方式对比

| 特性 | LiteDB | LocalFileStorage |
|------|--------|------------------|
| 存储格式 | 单文件数据库 | 多文件目录结构 |
| 读写性能 | ⭐⭐⭐ 高 | ⭐⭐ 中 |
| 事务支持 | ✅ 支持 | ❌ 不支持 |
| 批量操作 | ✅ 支持 | ❌ 不支持 |
| 备份难度 | ⭐ 简单（单文件） | ⭐⭐⭐ 复杂（多目录） |
| 数据查看 | 需专用工具 | 可直接查看 JSON 文件 |
| 适用场景 | 生产环境、大数据量 | 开发调试、简单场景 |

---

## 建议

- **生产环境**：推荐使用 **LiteDB**，性能和可靠性更好
- **开发调试**：可根据需要切换为 **LocalFileStorage**，便于直接查看和修改数据
- **数据迁移**：切换存储类型时建议先备份数据，或启用自动迁移功能
- **强制迁移**：仅在必要时使用，可能导致目标存储数据重复
