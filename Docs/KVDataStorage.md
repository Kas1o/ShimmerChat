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

## 使用传统文件存储

如需切换回传统的 LocalFileStorage 文件存储方式，请按以下步骤操作：

### 1. 修改配置文件

编辑 `appsettings.json`：

```json
{
  "KVDataStorage": {
    "StorageType": "LocalFileStorage",
    "AutoMigrateOnStartup": false,
    "AutoMigrateFrom": null,
    "ClearSourceAfterMigration": false
  }
}
```

### 2. 配置项说明

| 配置项 | 说明 | 可选值 |
|--------|------|--------|
| `StorageType` | 存储类型 | `"LiteDB"` / `"LocalFileStorage"` |
| `AutoMigrateOnStartup` | 启动时自动迁移 | `true` / `false` |
| `AutoMigrateFrom` | 迁移源存储类型 | `"LiteDB"` / `"LocalFileStorage"` / `null` |
| `ClearSourceAfterMigration` | 迁移后清空源数据 | `true` / `false` |

### 3. 数据迁移（可选）

如需将 LiteDB 中的数据迁移回文件存储：

```json
{
  "KVDataStorage": {
    "StorageType": "LocalFileStorage",
    "AutoMigrateOnStartup": true,
    "AutoMigrateFrom": "LiteDB",
    "ClearSourceAfterMigration": false
  }
}
```

启动应用后，数据将自动从 LiteDB 迁移到文件存储。

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
