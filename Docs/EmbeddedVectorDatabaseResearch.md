# C# / .NET 嵌入式向量数据库调研

> 调研时间：2026-11-06 ・ 目标运行时：.NET 10 ・ 场景：ShimmerChat（Blazor Server + Tauri 桌面，单机单用户）
>
> 本文所有性能数字均为本机实测（16 核 Windows x64，.NET 10.0.12，AVX2 / `Vector<float>.Count = 8`），
> 属于单机微基准，不代表厂商宣称值。

---

## 1. 结论摘要

1. **在 ShimmerChat 的量级（每 Agent 1k–50k 条记忆）上，不需要 ANN 索引。**
   1024 维、10 万条的内存暴力扫描单次查询约 **11 ms**，1 万条约 **0.95 ms**，且召回率 100%。
   "向量数据库"在这里真正的价值是**持久化 + 过滤 + 事务**，而不是索引算法。

2. **最贴合现状的落地方式是"内存扁平索引 + 现有 LiteDB 持久化"**：
   零新增原生依赖、零新进程、零打包风险，启动时从 LiteDB 载入向量重建内存数组即可。
   这也是把当前 Qdrant 外置服务从桌面发行版里拿掉的最短路径。

3. **想要 SQL / 单文件 / 更大数据量，选 SQLite + `sqlite-vec`**：
   实测 10 万条 768 维 KNN = **262 ms**，开启 `mmap_size` 后 **46 ms**（5.7 倍提升，必须开）。
   代价是每个 RID 一个原生 `vec0` 动态库，且 `sqlite-vec` 仍是 pre-v1。

4. **`LiteDB 6.0` 已原生内置 HNSW 向量索引**（PR 已合并，当前仅预发布）：
   `BsonVector` + `EnsureIndex(..., VectorIndexOptions(dim))` + `TopKNear` / `VECTOR_SIM`。
   它与现有存储栈同引擎，是中期最优雅的升级路径，但**现在还不能上生产**。

5. **不建议**把 LanceDB / USearch / DuckDB VSS 作为首选：能力虽强，但分别受制于
   119 MB 包体与平台覆盖、过薄的 C# 绑定、扩展安装与版本耦合。
   DotVector 仓库已归档，FAISS 系 .NET 绑定早已失修，服务型数据库（Qdrant/Chroma/Milvus/pgvector）
   与"嵌入式、免安装"的目标直接冲突。

---

## 2. 需求与约束

从代码现状看（`ShimmerChatBuiltin/Memory/MemoryToolV2.cs`、`MemoryRetrieveNode.cs`）：

- 记忆按 **Agent** 隔离（Qdrant 里一个 Agent 一个 collection）；
- 向量由 **OpenAI 兼容的 Embedding API** 产生，维度可配（默认 1024）；
- 检索发生在 **Pre-Generation 管线**里，每轮最多对最近 3 条消息各查一次；
- 宿主是 Blazor Server（多线程、单例服务）+ Tauri 桌面壳，**不能要求用户另装服务**；
- 现有持久化：LiteDB 5.0.21（KV + 消息），单文件、随应用走。

由此得到硬约束：

| 约束 | 说明 |
|------|------|
| 进程内 | 不允许"先启动一个数据库服务" |
| 可打包 | 原生依赖必须能随 Tauri 一起分发，覆盖目标 RID |
| 单文件/易备份 | 用户复制一个目录即可迁移 |
| 低延迟 | 单次检索最好 < 50 ms，避免拖慢首 token |
| 可过滤 | 至少能按 Agent（未来按会话、时间、标签）过滤 |
| 可演进 | 换 Embedding 模型/维度时能整体重建 |
| 许可 | MIT/Apache-2.0 优先，桌面分发不能有商业授权陷阱 |

规模假设：单 Agent 1k–50k 条，重度用户上限 10 万条；单条 768–1536 维 float32。

---

## 3. 候选全景

### 3.1 嵌入式（进程内）

| 方案 | 形态 | 索引 | 版本 / 状态 | 许可 | 原生依赖 |
|------|------|------|-------------|------|----------|
| **LiteDB 6** | 文档库内置向量 | HNSW | 6.0.0-prerelease.114（2026-09） | MIT | 无 |
| **SQLite + sqlite-vec** | SQL 扩展 | 暴力（vec0） | 0.1.10-alpha.4 / NuGet 0.1.7-alpha.2.1 | MIT/Apache-2.0 | 有（vec0） |
| **纯内存扁平索引** | 数组 + SIMD | 暴力 | 自写（`Vector<float>`） | — | 无 |
| **LanceDB C#** | 列存 + Arrow | IVF / HNSW | `LanceDB` 2.5.0（2026-07） | Apache-2.0 | 有（119 MB） |
| **Hnsw.Net** | 纯托管 ANN | HNSW | 0.1.4（2026-07） | MIT | 无 |
| **HNSWSharp** | 纯托管 ANN | HNSW | 活跃（2026-09） | MIT | 无 |
| **USearch** | 原生 ANN | HNSW | `Cloud.Unum.USearch` 2.26.6 | Apache-2.0 | 有（~5 MB） |
| **DuckDB + VSS** | 分析库扩展 | HNSW（USearch） | 扩展需 INSTALL/LOAD | MIT | 有（DuckDB + 扩展） |
| **DotVector** | 自研引擎 | Flat/HNSW/IVF | 1.0.0（2026-05），**仓库已归档** | MIT | 无 |

### 3.2 服务型（对照，不满足"嵌入式"）

Qdrant、Chroma、Milvus / Milvus Lite、Weaviate、pgvector、SQL Server 2025 vector、RavenDB（有 Embedded 包但商业授权）、Elasticsearch。

它们的共同问题不是能力，而是**部署形态**：要么独立进程，要么 Docker，要么需要用户装数据库。
对桌面端 Chat 应用，这会把"安装即用"变成"先配环境"，也正是当前 Qdrant 方案的痛点。

### 3.3 明确排除

- **DotVector**：NuGet 1.0.0 仅 200 次下载，GitHub 仓库 `archived=true`（2026-06 归档），不可作为依赖。
- **FAISS 系 .NET 绑定**：NuGet 上无稳定包，长期失修。
- **`Microsoft.SemanticKernel.Connectors.SqliteVec`**：已标记 deprecated，更名为
  `CommunityToolkit.VectorData.SqliteVec`（1.0.4-preview，2026-11-03），新项目不要再用旧包。

---

## 4. 重点方案深度对比

### 4.1 SQLite + `sqlite-vec`

**是什么**：`vec0` 虚拟表 + 一组 SQL 函数（`vec_distance_cosine`、`vec_quantize_binary` 等），
纯 C 实现、无依赖，支持 float32 / int8 / bit 三种向量，支持 metadata 列、partition key、auxiliary 列。

**能做什么**：

```sql
create virtual table vec_mem using vec0(
  memory_id integer primary key,
  agent text partition key,        -- 按 Agent 分片
  +content text,                   -- 大文本，不进 WHERE
  embedding float[1024] distance_metric=cosine
);

select memory_id, distance
from vec_mem
where embedding match :query and k = 5 and agent = '...'
order by distance;
```

**实测（768 维，k=5，WAL，BLOB 参数）**：

| 数据量 | 默认 | `mmap_size=1GB` + 256MB cache | 库文件 |
|--------|------|-------------------------------|--------|
| 2 万 | 55 ms | —（按线性推算 ~9 ms） | 60 MB |
| 10 万 | 262 ms | **46 ms** | 297 MB |

结论：**默认配置是 I/O 受限的，`mmap_size` 不是可选项而是必选项**。
即使调优后，它仍比内存扁平扫描慢约 4 倍（46 ms vs 11 ms @100k），但换来了 SQL 过滤、事务与崩溃安全。

**集成方式（已验证可行）**：

```csharp
using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
conn.LoadExtension(Path.Combine(AppContext.BaseDirectory,
    "runtimes", "win-x64", "native", "vec0.dll"));
```

或使用 MEVD 连接器 `CommunityToolkit.VectorData.SqliteVec`，走
`VectorStore` / `VectorStoreCollection<TKey,TRecord>` 抽象，代价是多一层依赖与预览版 API。

**坑**：

- `sqlite-vec` 的 NuGet 原生资产只覆盖 **win-x64 / linux-x64 / linux-arm64 / osx-x64 / osx-arm64**，
  **没有 win-arm64**；`.props` 里残留 win-x86/arm64 路径但实际不存在。
- `Microsoft.Data.Sqlite 10.0.10` 会拉入 `SQLitePCLRaw 2.1.11`，触发 **NU1903 高危漏洞告警**；
  需要显式引用 `SQLitePCLRaw.bundle_e_sqlite3 3.0.3+`（CommunityToolkit 包已是该版本）。
- `vec0` **没有 ANN 索引**，KNN 是分块暴力扫描；`partition key` 用不好会过度分片。
- `sqlite-vec` 仍是 pre-v1，README 明确"expect breaking changes"；NuGet 包装版本落后上游 3 个小版本。

**适用**：需要 SQL 过滤、想用单文件承载"记忆 + 元数据"、数据量 ≤ 10 万、能接受原生 DLL。

---

### 4.2 LiteDB 6（预发布）

**是什么**：LiteDB 在 6.0 里内置了向量能力（PR #2678，2025-09-30 合并，+9047 行）：

- 新 BSON 类型 `BsonVector`（`float[]`）；
- HNSW 向量索引，支持 `Cosine` / `Euclidean` / `DotProduct`；
- 查询扩展：`.TopKNear(x => x.Embedding, vector, k)`、`.WhereNear(...)`；
- 表达式/SQL：`VECTOR_SIM(field, @0)`；
- 引擎层（Insert/Update/Delete/DropCollection/Rebuild）已接入索引生命周期。

```csharp
using LiteDB.Vector;

docs.EnsureIndex(x => x.Embedding, new VectorIndexOptions(1024));
var top = docs.Query().TopKNear(x => x.Embedding, queryVector, k: 5).ToList();
```

**优势**：

- 与现有 LiteDB 5 存储栈同引擎，**不需要新增数据库、不需要原生 DLL**；
- 索引与文档在同一事务/文件里，不存在"索引与记录不同步"；
- 迁移路径清晰：升级 LiteDB 大版本即可。

**风险**：

- 目前只有 `6.0.0-prerelease.*`（最新 114，2026-09-13），稳定版仍是 5.0.21；
- 6.0 是大版本，API 与存储格式均可能变化，升级需要迁移与回归；
- 预发布 API 可能再变（`VectorIndexOptions` 的参数、过滤能力边界尚未定稿）。

**结论**：**中期首选，现在观望**。建议先用方案 A/B 落地，等 6.0 正式版发布后切过去，
届时可以删掉自己维护的索引代码。

---

### 4.3 纯内存扁平索引 + 现有持久化（推荐起步方案）

**做法**：向量以 `float[]` 连续数组放内存，查询时用 `System.Numerics.Vector<float>` 做 SIMD 点积；
持久化仍走 LiteDB（`byte[]` 存 float32 原始字节，避免 double 膨胀一倍）。
写入时归一化，查询也归一化，余弦相似度退化为点积。

**实测（1024 维，top-5，单线程）**：

| 数据量 | 中位耗时 | 内存 | 召回 |
|--------|----------|------|------|
| 1 万 | 0.95 ms | 39 MB | 100% |
| 3 万 | 3.65 ms | 117 MB | 100% |
| 10 万 | 11.26 ms | 390 MB | 100% |

耗时与 `N × D × 4 字节` 成正比——10 万 × 1024 维正好是 390 MB，单次查询要把这 390 MB 流式读一遍，
所以它是**内存带宽受限**的，而不是算力受限。这也解释了为什么它比 `sqlite-vec` 快：
后者还要过 SQL 虚表与页缓存。

**LiteDB 5 作为持久化层的实测**（`byte[]` 存 float32 原始字节，10k 条，含 Agent/时间/文本字段）：

| 维度 | 载荷/条 | 落盘/条 | 10k 文件 | 全量载入 10k | 插入 |
|------|---------|---------|----------|--------------|------|
| 768 | 3072 B | 4148 B | 39.6 MB | 140 ms | 0.05 ms/条 |
| 1024 | 4096 B | 8244 B | 78.6 MB | 156 ms | 0.05 ms/条 |
| 1536 | 6144 B | 8244 B | 78.6 MB | 162 ms | 0.04 ms/条 |

关键发现：LiteDB 5 的页大小是 **8 KB**，但它的数据页复用策略要求页内至少还剩 **4896 B**（约 60%）才会被再次分配
（`DataPage.FreeIndexSlot` 的五档空闲列表）。因此一条文档只要超过约 **3.2 KB**（8150 − 4896）就会独占整页：
1024 维（4096 B 载荷 + BSON 开销）刚好越线，落盘 8244 B/条；768 维（3072 B）还能两条挤一页，落盘 4148 B/条。
这不是向量被复制，而是页内碎片；1536 维同样是 8244 B/条，所以 1024 与 1536 的磁盘成本完全一样。
随机按 Id 读取约 0.07 ms/次。

要摊薄碎片有两条路：把多条向量打包进一个文档（每文档 8/32 条 × 1024 维，实测 **4143 B/vector**，
10k 文件 39.5 MB，接近 1:1），代价是更新要整段重写、需要定期压缩；或存 float16（2048 B/条，
重新落回"两条一页"，磁盘减半），代价是少量精度损失。

结论：LiteDB 5 **不是向量数据库**——没有向量类型、没有距离函数、没有 ANN 索引，
它的角色是"向量 BLOB 的持久化层"，查询效率全部来自进程内的扁平索引。
作为 blob store 它足够快（5 万条全量载入 0.8 s），但 10 万条 1024 维会得到约 800 MB 的文件 + 400 MB 内存，
超出桌面应用的合理范围。

**优势**：零依赖、零打包风险、精确召回、代码量约 100 行、AOT 友好。
**代价**：全量常驻内存；启动要重建；删除用"墓碑 + 定期压缩"；过滤只能靠额外的元数据数组。

**规模判断**：

- ≤ 2 万条：很舒服（1024 维约 165 MB 文件）；
- 2–5 万条：可接受，但文件会到约 400 MB，建议改存 float16 或分段打包；
- > 5 万条：换 `sqlite-vec`（10 万条 768 维实测仅 297 MB，接近 1:1）或等 LiteDB 6。

---

### 4.4 LanceDB C# SDK

**是什么**：社区维护的 C# 绑定（`lennylxx/lancedb-csharp`，17 stars），P/Invoke 包装官方 Rust crate，
API 覆盖度接近 Python SDK：表 CRUD、IVF-PQ/IVF-Flat/HNSW-PQ/HNSW-SQ、FTS、混合检索（RRF/线性/MRR）、
版本与 tag、命名空间、流式读取。

**实测/事实**：

- NuGet 包 **119 MB**（内含三平台原生库）；
- 原生库仅 **linux-x64 / win-x64 / osx-arm64**；
- 基于 Apache Arrow，写入需要构造 `RecordBatch` / `Schema`，**接入成本明显高于其他方案**；
- 官方 SDK 列表只有 Python / JS / Java / Rust，C# 属社区绑定。

**适用**：需要"向量 + 全文 + 混合排序 + 版本化"，且数据量在百万级、能接受包体与 Arrow 心智负担。
**不适用**：只想替掉 Qdrant 的聊天记忆——杀鸡用牛刀，且桌面分发体积不可接受。

---

### 4.5 纯托管 ANN：Hnsw.Net / HNSWSharp

**Hnsw.Net**（`ericstj/Hnsw.Net`，MIT，0.1.4，net10.0，纯托管、无 P/Invoke）：

- API 干净：`Add(id, span)`、`Search(span, k, filter)`、`Save/Load`、软删除、`ExportItems`；
- 自带 MEVD 连接器 `HnswVectorStore`；
- **实测（768 维）**：建索引 1.7 ms/条（1 万条 17 s；5 万条在 `Ef=100` 下约 148 s）；
  5 万条检索 1.29 ms/query；召回@10 在 1 万条上：`ef=50` 69%、`ef=100` 82%、`ef=400` 100%。

**判断**：

- 建索引成本太高，**不适合每次启动重建**；必须持久化索引文件；
- 默认 `ef` 下召回一般，要调参；
- 项目很新（3 stars、0.1.x），API 还会动。

结论：**可以作为一个"零原生依赖的 ANN 备选"，但不建议作为第一版的主力**。
在 5 万条以内，它的检索速度并不比暴力扫描快（1.29 ms vs 3.65 ms @3 万），却多了建索引与调参成本。

**HNSWSharp**（curiosity-ai，105 stars）更成熟一些，但同样需要自己解决持久化、过滤、并发写。

---

### 4.6 USearch

- `Cloud.Unum.USearch` 2.26.6，.NET Standard 2.0，原生 HNSW（C++），号称比 FAISS 快约 10 倍；
- NuGet 内含 **win-x64 / linux-x64 / osx-arm64** 原生库（`libusearch_c` + `numkong`，约 5 MB）；
- 支持量化（f16/bf16/i8/b1）、磁盘视图、过滤谓词。

问题在于 **C# 绑定很薄**：文档少、示例少、异常语义与生命周期要靠读源码；
且平台覆盖同样缺 win-arm64 / osx-x64。适合"愿意读 C++ 绑定源码、追求极致性能"的团队。

---

### 4.7 DuckDB + VSS

DuckDB 的 `vss` 扩展底层就是 USearch，SQL 体验不错，`DuckDB.NET` 也能用。
但扩展需要 `INSTALL vss; LOAD vss;`（首次联网）或手工分发扩展二进制，且**扩展版本必须与 DuckDB 版本严格匹配**。
对桌面应用来说，这是比 `sqlite-vec` 更脆的部署链路，不推荐。

---

## 5. 选型建议

### 5.1 推荐路线（按阶段）

| 阶段 | 方案 | 触发条件 |
|------|------|----------|
| **现在** | 内存扁平索引 + LiteDB 持久化（方案 A） | 默认，覆盖 95% 用户 |
| 现在（可选） | SQLite + `sqlite-vec`（方案 B） | 需要 SQL 过滤 / 单文件 / 数据量大 |
| 6.0 正式版后 | LiteDB 6 原生 HNSW（方案 C） | 替换 A/B 的自维护索引 |
| 特殊需求 | LanceDB C#（方案 D） | 需要 FTS + 混合排序、百万级 |

### 5.2 方案 A：内存扁平索引（建议先做）

在 `ShimmerChatLib` 定义抽象，把 `Qdrant.Client` 从节点里彻底剥离：

```csharp
public interface IMemoryVectorStore
{
    Task UpsertAsync(string agentId, MemoryRecord record, CancellationToken ct = default);
    Task DeleteAsync(string agentId, string memoryId, CancellationToken ct = default);
    Task<IReadOnlyList<MemoryHit>> SearchAsync(
        string agentId, ReadOnlyMemory<float> query, int topK,
        float minScore = 0f, CancellationToken ct = default);
}
```

- 实现一：`FlatMemoryVectorStore`（内存数组 + `Vector<float>` 点积）；
- 实现二（后续）：`SqliteVecMemoryStore`、`LiteDb6MemoryStore`；
- 记录：`{ MemoryId, AgentId, Content, Vector(byte[]), Dim, Model, CreatedAt }`；
- 启动时按 Agent 懒加载，写入后增量更新内存；删除用墓碑，占比超过 30% 时压缩。

**收益**：去掉 Qdrant 依赖、去掉端口/API Key 配置、离线可用、检索 < 5 ms。

### 5.3 方案 B：SQLite + `sqlite-vec`

如果希望"记忆就是一张表"、要按时间/标签过滤、或单 Agent 超过 5 万条：

```csharp
// 建表：距离必须显式指定 cosine，否则默认 L2
create virtual table vec_mem using vec0(
  memory_id text primary key,
  agent text partition key,
  created_at integer,
  +content text,
  embedding float[1024] distance_metric=cosine
);

// 连接后立刻设置，否则 10 万条要 260 ms
pragma journal_mode=wal;
pragma mmap_size=1073741824;
pragma cache_size=-262144;
```

注意：`k` 必须出现在 `WHERE` 里（`and k = 5`）；元数据过滤只能用 `= != > >= < <=`；
不要用 `LIKE` / `IS NULL`（会报错或结果错误）。

### 5.4 方案 C：等 LiteDB 6

把 `IMemoryVectorStore` 的实现换成 LiteDB 6 的 `TopKNear` 即可，上层节点无感。
迁移前先做两件事：确认 6.0 的存储格式迁移路径；用 1 万条真实记忆跑一轮召回对比。

---

## 6. 与现有代码的衔接点

1. **抽象先行的理由**：`MemoryRetrieveNode` 与 `MemoryToolV2` 目前各自 `new QdrantClient(...)`，
   连接参数、collection 命名、维度读取散落在两处。抽 `IMemoryVectorStore` 后，
   Qdrant 只是一个实现，未来换 SQLite/LiteDB 不改节点。

2. **维度与模型要落库**：换 Embedding 模型（1024 → 1536）必须整体重建。
   建议在记忆库里存 `Model` / `Dim` / `Normalized` 三个字段，启动时校验，不一致直接报错而不是静默混用。

3. **归一化统一在一处**：写入与查询都做 L2 归一化，距离统一用"1 - 点积"，
   避免 Qdrant 的 Cosine 与本地实现的阈值语义不一致。

4. **并发**：Blazor Server 是单例 + 多线程。SQLite 用"每次操作一个连接 + WAL"，
   不要跨 `await` 持有连接；LiteDB 5 本身线程安全，但 `float[]` 内存索引要加读写锁。

5. **打包（Tauri）**：
   - 选 B 时必须确保 `vec0.dll` 进产物。`PackageReference` 会把它放在
     `runtimes/win-x64/native/`，`LoadExtension` 要用这个路径；
   - 若用 single-file 发布，需要 `IncludeNativeLibrariesForSelfExtract=true`，
     并验证解压目录中的 `vec0.dll` 能被找到；
   - macOS 分发要对 `.dylib` 一并签名/公证。

6. **安全**：`LoadExtension` 等于加载任意原生代码，路径必须固定为
   `AppContext.BaseDirectory` 下的已知文件，绝不能来自配置或用户输入。

7. **顺带发现**：`MemoryRetrieveNode` 里的 `static Dictionary<string, List<float>> _vectorCache`
   以完整消息文本为 key 且无上限，长会话下会持续增长；改成有界 LRU 或直接去掉。

---

## 7. 决策矩阵

评分 1–5，5 为最好。

| 维度 | 内存扁平 | sqlite-vec | LiteDB 6 | LanceDB | Hnsw.Net | USearch |
|------|---------|-----------|----------|---------|----------|---------|
| 免安装/免进程 | 5 | 5 | 5 | 5 | 5 | 5 |
| 集成成本（低=好） | 5 | 4 | 4 | 2 | 3 | 2 |
| 10k 查询延迟 | 5 | 4 | 4* | 3 | 4 | 5 |
| 100k 查询延迟 | 3 | 4（开 mmap） | 4* | 5 | 4 | 5 |
| 精确召回 | 5 | 5 | 4 | 4 | 3–4 | 4 |
| 过滤/事务 | 2 | 5 | 4 | 4 | 2 | 2 |
| 打包体积 | 5 | 4 | 5 | 1 | 5 | 4 |
| 平台覆盖 | 5 | 3（无 win-arm64） | 5 | 2 | 5 | 3 |
| 成熟度 | 5（自写） | 3（pre-v1） | 2（预发布） | 3（社区绑定） | 2 | 3 |
| 许可 | — | MIT/Apache | MIT | Apache | MIT | Apache |

\* LiteDB 6 的延迟为预期值（HNSW + 内存页缓存），未实测。

---

## 8. 风险与待验证清单

- [ ] 用真实记忆数据（而非随机向量）复测召回：随机向量的近邻分布与语义向量差异很大。
- [ ] 复测 `sqlite-vec` 的 `chunk_size` 与 `page_size` 调优空间（官方 performance 文档仍是 TODO）。
- [ ] 在 Tauri single-file 产物中验证 `vec0.dll` 加载与 `mmap_size` 行为。
- [ ] 评估 LiteDB 5 → 6 的数据迁移与回滚方案。
- [ ] 评估 1536 维下的内存占用（10 万条 ≈ 585 MB），必要时用 int8 / float16 量化。
- [ ] 若走 MEVD 抽象，确认 `CommunityToolkit.VectorData.SqliteVec` 的预览版 API 稳定性。

---

## 9. 参考

- sqlite-vec：[GitHub](https://github.com/asg017/sqlite-vec) ・ [文档](https://alexgarcia.xyz/sqlite-vec/) ・ [vec0 表](https://alexgarcia.xyz/sqlite-vec/features/vec0.html) ・ [NuGet](https://www.nuget.org/packages/sqlite-vec)
- MEVD / 向量存储概览：[Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/ai/vector-stores/overview)
- CommunityToolkit 连接器（含 SqliteVec）：[GitHub](https://github.com/CommunityToolkit/AI) ・ [NuGet](https://www.nuget.org/packages/CommunityToolkit.VectorData.SqliteVec)
- LiteDB 向量索引 PR：[#2678](https://github.com/litedb-org/LiteDB/pull/2678) ・ [LiteDB NuGet](https://www.nuget.org/packages/LiteDB)
- LanceDB C#：[NuGet](https://www.nuget.org/packages/LanceDB) ・ [源码](https://github.com/lennylxx/lancedb-csharp)
- Hnsw.Net：[NuGet](https://www.nuget.org/packages/Hnsw.Net) ・ [源码](https://github.com/ericstj/Hnsw.Net)
- HNSWSharp：[GitHub](https://github.com/curiosity-ai/hnsw-sharp)
- USearch：[NuGet](https://www.nuget.org/packages/Cloud.Unum.USearch) ・ [文档](https://unum-cloud.github.io/USearch/csharp)
