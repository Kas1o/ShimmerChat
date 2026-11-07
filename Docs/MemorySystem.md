# 记忆与向量存储

ShimmerChat 的长期记忆由两层组成：

1. **记忆文本库**（真值）：`Memory/records.db`，LiteDB 单文件，保存内容、标签、版本与来源。
2. **向量索引**（派生）：按嵌入模型分成不同集合，写入由 `IVectorStoreService` 的实现负责。

删掉向量目录不会丢记忆文本；重建索引即可从文本重新生成向量。

---

## 1. 核心类型

| 类型 | 位置 | 职责 |
|------|------|------|
| `IVectorStoreService` | `ShimmerChatLib/Interface` | 集合/记录/检索抽象，不包含记忆领域概念 |
| `VectorStoreConfig` | `ShimmerChatLib/Models` | `appsettings.json` 的 `VectorStore` 节 |
| `MemoryRecordStore` | `ShimmerChatBuiltin/Memory` | 记忆文本真值库 |
| `MemoryIndexService` | `ShimmerChatBuiltin/Memory` | 文本 ↔ 向量写穿、对账、构建与检索 |
| `MemoryModelConfig` | `ShimmerChatBuiltin/Memory` | 当前嵌入配置与 model key / collection 名 |
| `MemoryPartition` | `ShimmerChatBuiltin/Memory` | 记忆按 Agent 分区的唯一格式化入口 |

`PersistentEnv.VectorStore` 为节点/工具提供向量存储；为 `null` 时必须明确失败。

---

## 2. 存储布局

```
AppContext.BaseDirectory/
├── Memory/records.db        # 记忆文本真值，始终落盘
└── VectorStore/             # 由 VectorStore:RootPath 决定
    ├── default.db
    └── plugin_<id>-<hash>.db
```

- 记忆文本库路径目前固定，不随 `VectorStore:RootPath` 改变。
- 备份/迁移时需要同时保留 `Memory/` 与 `VectorStore/`（若后端为 `LiteDb`）。
- 把后端切换为 `InMemory` 只会让**向量**不落盘，记忆文本仍在 `Memory/records.db`。

### appsettings.json

```json
{
  "VectorStore": {
    "Provider": "LiteDb",
    "RootPath": "VectorStore",
    "Scope": "PerOwner"
  }
}
```

- `Provider`：`LiteDb` 或 `InMemory`，未知值在启动阶段直接失败。
- `Scope`：`PerOwner`（每个 owner 一个文件）或 `PerCollection`（每个集合一个文件）。
- 后端由启动配置决定，运行中不可切换；设置页只做只读展示。

---

## 3. 模型与集合

- 一个嵌入配置 = 一份 `BaseUrl + ModelName`，其 `ModelKey`（SHA-256）决定集合名
  `memory_<modelKey 前 16 位>`。
- 集合维度/度量/模型不一致时写入直接失败，不静默混用。
- 记忆记录按 Agent 分区（`MemoryPartition.ForAgent`），同一模型的所有 Agent 共用一个集合。
- 切换嵌入配置不会自动重建索引；设置页可分别查看、构建或删除每个模型索引。

---

## 4. 就绪状态与失败语义

`MemoryIndexService.Snapshot()` 只看持久化状态；`GetStatusAsync()` 会进一步校验：

- 当前 `IVectorStoreService.StoreIdentity` 是否与构建时一致（Provider / RootPath 变化）；
- 向量集合是否存在、记录数是否与文本库一致；
- 对指定 Agent，全局 `Partial/Suspect` 时会单独对账该 Agent 的分区。

`MemoryIndexService.SearchAsync()` 在检索前执行同样的校验；向量集合在检索瞬间消失时，
`CollectionNotFound` / `DimensionMismatch` 会被转换成 `MemoryNotReadyException`，而不是通用错误。

- `MemoryRetrieveNode`：索引未就绪时返回 `NodeResult.Failure`，终止本轮生成。
- `MemoryToolV2`：索引未就绪时返回 `[Tool error]` 文本，不中断生成。
- 删除记忆文本后，向量若暂时删不掉会成为孤儿，由设置页的「清理孤儿向量」或构建任务清理。

---

## 5. 嵌入配置管理

`EmbeddingProfileStore` 把多份配置保存在 KV 空间 `EmbeddingAPI`：

- `profiles`：JSON 数组；
- `active`：当前激活配置 id；
- 旧版 `uri` / `apikey` / `modelname` 会自动迁移为一条 `default` 配置。

`MemoryRetrieveNode.EmbeddingProfileId` 可把节点固定到某份配置；为空则跟随当前激活配置。
`MemoryModelConfig.Load` 在「配置数据损坏」「激活配置不存在」时返回明确错误，而不是回落到第一份配置。

---

## 6. 从 Qdrant 升级

本版本不再依赖 Qdrant，也不会自动迁移旧 Qdrant 集合中的记忆。升级后需要在设置中重新配置嵌入服务并构建索引；
旧的 Qdrant 数据仍保留在原服务中，不会被删除。

---

## 7. 主要页面

- **Settings → 记忆与向量存储**：概览、模型索引、后端与维护（构建/校验/清理孤儿/删除索引）。
- **Agent 面板 → 记忆**：当前 Agent 的记忆列表、增删改、关键词/语义检索、单 Agent 同步。
- **Chat 面板 → 记忆**：当前会话的索引状态、最近一次召回结果与快速检索。
