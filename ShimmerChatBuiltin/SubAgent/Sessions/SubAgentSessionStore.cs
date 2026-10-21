using System.Collections.Concurrent;
using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.SubAgent.Sessions
{
    /// <summary>会话读取结果状态。</summary>
    public enum SubAgentSessionLoadStatus
    {
        Ok,
        NotFound,
        /// <summary>数据存在但无法解析（或读取失败）。调用方应报错，不得当成「不存在」静默新建覆盖。</summary>
        Corrupted
    }

    /// <summary>只读读取结果（Viewer 使用）。</summary>
    public sealed class SubAgentSessionReadResult
    {
        public required SubAgentSessionLoadStatus Status { get; init; }
        public SubAgentSession? Session { get; init; }
        public string? Error { get; init; }
    }

    /// <summary>打开/新建会话的结果，成功时携带已持锁的执行作用域。</summary>
    public sealed class SubAgentSessionOpenResult
    {
        public required SubAgentSessionLoadStatus Status { get; init; }
        public SubAgentSessionRunScope? Scope { get; init; }
        public string? Error { get; init; }

        public SubAgentSession? Session => Scope?.Session;
    }

    /// <summary>
    /// 子智能体会话的私有存储。
    /// <para>
    /// 布局（KVData 空间 <c>SubAgent</c>）：<c>sessions</c> 存索引（<see cref="SubAgentSessionSummary"/> 列表），
    /// <c>session_{guid:N}</c> 存单个会话全量 JSON。<see cref="IKVDataService"/> 既没有 Delete 也没有枚举，
    /// 所以删除 = 从索引移除 + 把该键写成空字符串。
    /// </para>
    /// <para>
    /// 并发：会话级 <see cref="SemaphoreSlim"/> 串行化「读 → 追加 → 写」，索引读改写走
    /// <see cref="IndexLock"/>（与 <c>Agent.Update</c> 同一思路）。工具实例每轮生成都会重建，
    /// 因此锁必须是进程级的。
    /// </para>
    /// </summary>
    public static class SubAgentSessionStore
    {
        public const string SpaceId = "SubAgent";
        public const string IndexKey = "sessions";
        public const string SessionKeyPrefix = "session_";

        private static readonly object IndexLock = new();
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SessionLocks = new();

        /// <summary>会话键名。不使用 <c>:</c>，文件存储会把非法字符替换掉。</summary>
        public static string SessionKey(Guid sessionId) => $"{SessionKeyPrefix}{sessionId:N}";

        #region 查询

        /// <summary>只读读取单个会话（不加会话锁，供 Viewer 使用）。</summary>
        public static SubAgentSessionReadResult Load(IKVDataService kvData, IDebugOutputService? debugOutput, Guid sessionId)
        {
            string? json;
            try
            {
                json = kvData.Read(SpaceId, SessionKey(sessionId));
            }
            catch (Exception ex)
            {
                return Corrupted(debugOutput, sessionId, $"read failed: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(json))
                return new SubAgentSessionReadResult { Status = SubAgentSessionLoadStatus.NotFound };

            try
            {
                var session = JsonConvert.DeserializeObject<SubAgentSession>(json);
                if (session == null)
                    return Corrupted(debugOutput, sessionId, "session json deserialized to null");
                return new SubAgentSessionReadResult { Status = SubAgentSessionLoadStatus.Ok, Session = session };
            }
            catch (Exception ex)
            {
                return Corrupted(debugOutput, sessionId, $"json parse failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 读取会话索引。索引损坏时返回错误而不是空列表，避免调用方把「坏索引」当成「没有会话」后覆盖写入。
        /// </summary>
        public static (IReadOnlyList<SubAgentSessionSummary> Summaries, string? Error) List(
            IKVDataService kvData, IDebugOutputService? debugOutput, Guid? configGuid = null)
        {
            var index = ReadIndex(kvData);
            if (index.Error != null)
            {
                debugOutput?.Write("SubAgentSessionStore", "Index",
                    $"[SubAgentSessionStore] Session index unavailable: {index.Error}");
                return (Array.Empty<SubAgentSessionSummary>(), index.Error);
            }

            IEnumerable<SubAgentSessionSummary> summaries = index.Summaries!;
            if (configGuid != null)
                summaries = summaries.Where(s => s.ConfigGuid == configGuid);

            return (summaries.OrderByDescending(s => s.UpdatedAt).ToList(), null);
        }

        #endregion

        #region 打开 / 新建 / 删除

        /// <summary>打开已有会话并持有会话锁；调用方用完必须 Dispose 作用域。</summary>
        public static SubAgentSessionOpenResult Open(IKVDataService kvData, IDebugOutputService? debugOutput, Guid sessionId)
        {
            // 先拿锁再读：否则两个并发续聊会各自基于同一份旧快照写回，后写的丢掉前者的追加
            var gate = AcquireGate(sessionId);

            SubAgentSessionReadResult read;
            try
            {
                read = Load(kvData, debugOutput, sessionId);
            }
            catch
            {
                gate.Release();
                throw;
            }

            if (read.Status != SubAgentSessionLoadStatus.Ok)
            {
                gate.Release();
                return new SubAgentSessionOpenResult
                {
                    Status = read.Status,
                    Error = read.Error
                };
            }

            return new SubAgentSessionOpenResult
            {
                Status = SubAgentSessionLoadStatus.Ok,
                Scope = new SubAgentSessionRunScope(kvData, debugOutput, read.Session!, gate)
            };
        }

        /// <summary>新建会话并持有会话锁；首次落盘失败只记录在作用域上，不阻断本次调用。</summary>
        public static SubAgentSessionOpenResult StartNew(
            IKVDataService kvData, IDebugOutputService? debugOutput, SubAgentConfig config,
            string title, Message firstMessage, Guid? parentChatGuid, Guid? parentAgentGuid)
        {
            var session = new SubAgentSession
            {
                ConfigGuid = config.Guid,
                ConfigName = config.Name,
                Title = title,
                ParentChatGuid = parentChatGuid,
                ParentAgentGuid = parentAgentGuid,
                Messages = [firstMessage]
            };

            var gate = AcquireGate(session.Id);
            var scope = new SubAgentSessionRunScope(kvData, debugOutput, session, gate);
            scope.Persist();

            return new SubAgentSessionOpenResult
            {
                Status = SubAgentSessionLoadStatus.Ok,
                Scope = scope
            };
        }

        /// <summary>会话正被一次进行中的调用占用（Viewer 删除时立即返回，不阻塞界面）。</summary>
        public const string SessionInUseError =
            "session is currently in use by a running sub-agent call; retry after it finishes";

        /// <summary>
        /// 删除一个会话。返回 null 表示成功，否则为错误说明。
        /// 会话正被进行中的调用持有时<strong>不阻塞等待</strong>，直接返回
        /// <see cref="SessionInUseError"/>——否则界面线程会被一次长时间的子代理调用卡住。
        /// </summary>
        public static string? Delete(IKVDataService kvData, IDebugOutputService? debugOutput, Guid sessionId)
        {
            var gate = SessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
            if (!gate.Wait(0))
                return SessionInUseError;

            try
            {
                var index = ReadIndex(kvData);
                if (index.Error != null)
                    return index.Error;

                var list = index.Summaries!;
                list.RemoveAll(s => s.Id == sessionId);
                WriteIndex(kvData, list);

                // IKVDataService 没有 Delete：写入空值即视为不存在
                kvData.Write(SpaceId, SessionKey(sessionId), "");
                return null;
            }
            catch (Exception ex)
            {
                debugOutput?.Write("SubAgentSessionStore", "Delete",
                    $"[SubAgentSessionStore] Failed to delete session {sessionId:N}: {ex}");
                return ex.Message;
            }
            finally
            {
                // 不回收 SemaphoreSlim：与仍持有旧实例的调用方共用两个 gate 会导致并发写丢数据
                gate.Release();
            }
        }

        /// <summary>删除某个 SubAgent 配置的全部会话，返回实际删除数量与首个错误。</summary>
        public static (int Deleted, string? Error) DeleteAllForConfig(
            IKVDataService kvData, IDebugOutputService? debugOutput, Guid configGuid)
        {
            var (summaries, error) = List(kvData, debugOutput, configGuid);
            if (error != null)
                return (0, error);

            var deleted = 0;
            string? firstError = null;
            foreach (var summary in summaries)
            {
                var deleteError = Delete(kvData, debugOutput, summary.Id);
                if (deleteError != null)
                    firstError ??= deleteError;
                else
                    deleted++;
            }

            return (deleted, firstError);
        }

        #endregion

        #region 落盘

        /// <summary>
        /// 写入会话 JSON 并刷新索引。返回 null 表示成功。
        /// 索引损坏时<strong>不覆盖</strong>索引，直接返回错误（由调用方以 persisted=false 汇报）。
        /// </summary>
        internal static string? WriteSession(IKVDataService kvData, SubAgentSession session)
        {
            lock (IndexLock)
            {
                var index = ReadIndex(kvData);
                if (index.Error != null)
                    return index.Error;

                string json;
                try
                {
                    json = JsonConvert.SerializeObject(session);
                }
                catch (Exception ex)
                {
                    return $"session serialization failed: {ex.Message}";
                }

                try
                {
                    kvData.Write(SpaceId, SessionKey(session.Id), json);

                    var list = index.Summaries!;
                    var summary = session.ToSummary();
                    var existing = list.FindIndex(s => s.Id == session.Id);
                    if (existing >= 0)
                        list[existing] = summary;
                    else
                        list.Insert(0, summary);

                    WriteIndex(kvData, list);
                    return null;
                }
                catch (Exception ex)
                {
                    return $"session write failed: {ex.Message}";
                }
            }
        }

        private static void WriteIndex(IKVDataService kvData, List<SubAgentSessionSummary> summaries)
        {
            kvData.Write(SpaceId, IndexKey, JsonConvert.SerializeObject(summaries));
        }

        private sealed class IndexReadResult
        {
            public List<SubAgentSessionSummary>? Summaries { get; init; }
            public string? Error { get; init; }
        }

        private static IndexReadResult ReadIndex(IKVDataService kvData)
        {
            string? json;
            try
            {
                json = kvData.Read(SpaceId, IndexKey);
            }
            catch (Exception ex)
            {
                return new IndexReadResult { Error = $"session index read failed: {ex.Message}" };
            }

            if (string.IsNullOrWhiteSpace(json))
                return new IndexReadResult { Summaries = [] };

            try
            {
                var list = JsonConvert.DeserializeObject<List<SubAgentSessionSummary>>(json);
                if (list == null)
                    return new IndexReadResult { Error = "session index json deserialized to null" };
                return new IndexReadResult { Summaries = list };
            }
            catch (Exception ex)
            {
                return new IndexReadResult { Error = $"session index json is corrupted: {ex.Message}" };
            }
        }

        private static SubAgentSessionReadResult Corrupted(
            IDebugOutputService? debugOutput, Guid sessionId, string error)
        {
            debugOutput?.Write("SubAgentSessionStore", "SessionLoad",
                $"[SubAgentSessionStore] Failed to load session {sessionId:N}: {error}");
            return new SubAgentSessionReadResult { Status = SubAgentSessionLoadStatus.Corrupted, Error = error };
        }

        private static SemaphoreSlim AcquireGate(Guid sessionId)
        {
            var gate = SessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
            gate.Wait();
            return gate;
        }

        #endregion
    }

    /// <summary>
    /// 一次子智能体调用的会话作用域：持有会话锁，负责把消息与最终状态写回存储。
    /// 作用域存活期间独占该会话，Viewer 的删除会阻塞到作用域释放。
    /// </summary>
    public sealed class SubAgentSessionRunScope : IDisposable
    {
        private readonly IKVDataService _kvData;
        private readonly IDebugOutputService? _debugOutput;
        private readonly SemaphoreSlim _gate;
        private bool _disposed;

        public SubAgentSession Session { get; }

        /// <summary>最近一次落盘失败的原因；null 表示全部成功。</summary>
        public string? PersistError { get; private set; }

        internal SubAgentSessionRunScope(
            IKVDataService kvData, IDebugOutputService? debugOutput, SubAgentSession session, SemaphoreSlim gate)
        {
            _kvData = kvData;
            _debugOutput = debugOutput;
            Session = session;
            _gate = gate;
        }

        /// <summary>追加一条消息并落盘。</summary>
        public bool Append(Message message)
        {
            Session.Messages.Add(message);
            Session.UpdatedAt = DateTime.Now;
            return Persist();
        }

        /// <summary>标记成功结束并落盘。</summary>
        public bool Complete(string? outputText)
        {
            Session.Status = SubAgentSessionStatus.Completed;
            Session.OutputText = outputText;
            Session.ErrorCode = null;
            Session.ErrorMessage = null;
            Session.UpdatedAt = DateTime.Now;
            return Persist();
        }

        /// <summary>标记失败结束并落盘（失败现场同样要留下，便于排查）。</summary>
        public bool Fail(string code, string? errorMessage)
        {
            Session.Status = SubAgentSessionStatus.Failed;
            Session.ErrorCode = code;
            Session.ErrorMessage = errorMessage;
            Session.UpdatedAt = DateTime.Now;
            return Persist();
        }

        /// <summary>整段重写会话 JSON 与索引。</summary>
        public bool Persist()
        {
            var error = SubAgentSessionStore.WriteSession(_kvData, Session);
            if (error == null)
                return true;

            PersistError = error;
            _debugOutput?.Write("SubAgentSessionStore", "Persist",
                $"[SubAgentSessionStore] Failed to persist session {Session.Id:N}: {error}");
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _gate.Release();
        }
    }
}
