using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.ChatView;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Creator
{
    /// <summary>
    /// Creator 工具集：智能体（Agent）管理。让模型以结构化数据完成
    /// 新建 / 列出 / 查看 / 修改 / 删除智能体，并读取或替换其三条私有管线树。
    /// <para>
    /// 契约要点：
    /// <list type="bullet">
    /// <item>部分字段更新一律经 <see cref="Agent.Update(Guid, IKVDataService, Action{Agent})"/>，
    /// 避免 Blazor Server 多电路下的「最后写入覆盖」；</item>
    /// <item>管线树写入前先经对应管线序列化器校验（类型白名单 + 懒迁移），
    /// 与运行期反序列化是同一套代码，不存在第二份校验规则；</item>
    /// <item>修改当前会话所属的智能体时同步写穿到宿主持有的活实例（PersistentEnv.Agent
    /// 与 UI 共享同一实例），否则下一轮生成仍会使用旧树；</item>
    /// <item>所有失败返回带错误码的 <see cref="ToolResult"/>，不吞异常、不静默修补。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class CreatorAgentTool : IAutoCreateToolV2, IToolV3
    {
        private readonly IKVDataService _kvData;
        private readonly ILocService _loc;
        private readonly IDebugOutputService _debug;
        private readonly IServiceProvider? _services;
        private readonly Agent _liveAgent;

        public static string NameKey => "tool.creator_agent";
        public static string DescriptionKey => "tool.creator_agent.desc";
        public static string[] CategoryKeys => ["category.creator"];

        public CreatorAgentTool(PersistentEnv env)
        {
            _kvData = env.KVData;
            _loc = env.LocService;
            _debug = env.DebugOutput;
            _services = env.Services;
            _liveAgent = env.Agent;
        }

        public static IAutoCreateToolV2 Create(PersistentEnv env) => new CreatorAgentTool(env);

        public Tool GetDefinition() => new()
        {
            name = "creator_agent",
            description =
                "Manage ShimmerChat agents (top-level AI personas) and their pipeline trees. " +
                "Actions: 'list' all agents (guid, name, tags, chat count, which private trees exist); " +
                "'get' one agent's full settings {agent_id}; 'create' {name, user_intro?, greeting?, " +
                "alternative_greetings?, tags?}; 'update' {agent_id, name?, user_intro?, greeting?, tags?, " +
                "chat_view_id?, alternative_greetings?} — only fields present in the arguments are changed, " +
                "an explicit null clears greeting or chat_view_id; 'delete' {agent_id} removes the agent from " +
                "the registry while its chats stay in storage (this mirrors the UI delete; confirm with the " +
                "user first); 'get_pipeline' {agent_id, pipeline} returns the agent's private tree JSON for " +
                "'pre' | 'post' | 'render'; 'set_pipeline' {agent_id, pipeline, tree_json} validates then stores " +
                "a private tree — an empty or '{}' tree_json clears it so the global/default pipeline applies; " +
                "'list_chat_views' shows the ids usable as chat_view_id. " +
                "Tree JSON is a wrapper object {\"Id\",\"Name\",\"IsExpanded\",\"IsBypassed\",\"Node\":{...}} where " +
                "Node holds the business node with \"$type\" set to its full type name; use " +
                "creator_node_catalog to discover node types and properties.",
            parameters =
            [
                (new ToolParameter
                {
                    name = "action",
                    type = ParameterType.String,
                    description = "Operation to perform.",
                    @enum = ["list", "get", "create", "update", "delete", "get_pipeline", "set_pipeline", "list_chat_views"]
                }, true),
                (new ToolParameter
                {
                    name = "agent_id",
                    type = ParameterType.String,
                    description = "Target agent GUID (returned by 'list' and 'create')."
                }, false),
                (new ToolParameter
                {
                    name = "name",
                    type = ParameterType.String,
                    description = "Agent display name (create/update)."
                }, false),
                (new ToolParameter
                {
                    name = "user_intro",
                    type = ParameterType.String,
                    description = "Introduction text shown to the user (create/update)."
                }, false),
                (new ToolParameter
                {
                    name = "greeting",
                    type = ParameterType.String,
                    description = "First assistant message on a new chat (create/update; null clears)."
                }, false),
                (new ToolParameter
                {
                    name = "alternative_greetings",
                    type = ParameterType.Array,
                    description = "Extra greeting variants (create/update)."
                }, false),
                (new ToolParameter
                {
                    name = "tags",
                    type = ParameterType.Array,
                    description = "Tags for filtering (create/update)."
                }, false),
                (new ToolParameter
                {
                    name = "chat_view_id",
                    type = ParameterType.String,
                    description = "Chat view id from list_chat_views; null uses the default view (update)."
                }, false),
                (new ToolParameter
                {
                    name = "pipeline",
                    type = ParameterType.String,
                    description = "Pipeline for get_pipeline / set_pipeline.",
                    @enum = ["pre", "post", "render"]
                }, false),
                (new ToolParameter
                {
                    name = "tree_json",
                    type = ParameterType.String,
                    description = "Node tree JSON to store; empty or '{}' clears the private tree (set_pipeline)."
                }, false)
            ]
        };

        public Task<ToolResult> ExecuteAsync(ToolInvocationContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var args = context.TryGetArgumentsObject();
            if (args == null)
                return Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    $"Arguments for tool '{context.ToolName}' are not a valid JSON object."));

            var action = args["action"]?.Type == JTokenType.String
                ? args["action"]!.Value<string>()!.Trim()
                : null;
            if (string.IsNullOrEmpty(action))
                return Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments, "'action' is required."));

            return action switch
            {
                "list" => Task.FromResult(ListAgents()),
                "get" => Task.FromResult(GetAgent(args)),
                "create" => Task.FromResult(CreateAgent(args)),
                "update" => Task.FromResult(UpdateAgent(args)),
                "delete" => Task.FromResult(DeleteAgent(args)),
                "get_pipeline" => Task.FromResult(GetPipeline(args)),
                "set_pipeline" => Task.FromResult(SetPipeline(args)),
                "list_chat_views" => Task.FromResult(ListChatViews()),
                _ => Task.FromResult(ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    $"Unknown action '{action}'."))
            };
        }

        // ------------------------------------------------------------------
        // 读取
        // ------------------------------------------------------------------

        private ToolResult ListAgents()
        {
            List<Guid> guids;
            try
            {
                guids = Agent.GetAllAgentGuids(_kvData);
            }
            catch (Exception ex)
            {
                return ToolResult.Fail(ToolErrorCodes.ServiceError,
                    "The agent registry (__AllAgents__) is unreadable.", ex.ToString());
            }

            var agents = new List<object>();
            var unreadable = new List<string>();
            foreach (var guid in guids)
            {
                try
                {
                    var agent = Agent.Load(guid, _kvData);
                    agents.Add(Summarize(agent));
                }
                catch (Exception ex)
                {
                    // 注册表里挂着无法加载的记录是数据问题：不隐藏，进 unreadable 列表 + 日志。
                    unreadable.Add(guid.ToString());
                    _debug.Write("CreatorAgentTool", "List",
                        $"Failed to load agent '{guid}': {ex.Message}");
                }
            }

            return ToolResult.Ok(CreatorShared.Json(new
            {
                count = agents.Count,
                agents,
                unreadable
            }));
        }

        private ToolResult GetAgent(JObject args)
        {
            if (!TryGetAgentId(args, out var agentId, out var idError))
                return idError;

            if (!TryLoadAgent(agentId, out var agent, out var failure))
                return failure;

            return ToolResult.Ok(CreatorShared.Json(new
            {
                agent_id = agent.Guid,
                name = agent.Name,
                user_intro = agent.UserIntro,
                greeting = agent.Greeting,
                alternative_greetings = agent.AlternativeGreetings,
                tags = agent.Tags,
                chat_view_id = agent.ChatViewId,
                avatar_guid = agent.AvatarGuid,
                background_guid = agent.BackgroundGuid,
                chat_count = agent.ChatGuids.Count,
                pipelines = PipelineFlags(agent)
            }));
        }

        // ------------------------------------------------------------------
        // 元数据 CRUD
        // ------------------------------------------------------------------

        private ToolResult CreateAgent(JObject args)
        {
            if (!CreatorShared.TryGetStringArg(args, "name", out var name, out var nameError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, nameError!);
            if (string.IsNullOrWhiteSpace(name))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    "'name' is required and must not be empty.");

            if (!CreatorShared.TryGetStringArg(args, "user_intro", out var intro, out var introError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, introError!);
            if (!CreatorShared.TryGetStringArg(args, "greeting", out var greeting, out var greetingError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, greetingError!);

            if (!CreatorShared.TryGetStringArray(args, "alternative_greetings",
                    out var alternativeGreetings, out var greetingsError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, greetingsError!);
            if (!CreatorShared.TryGetStringArray(args, "tags", out var tags, out var tagsError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, tagsError!);

            var agent = Agent.Create(name!.Trim(), intro ?? "", greeting, alternativeGreetings);
            agent.Tags = tags ?? new List<string>();
            agent.Save(_kvData);
            Agent.AddAgentToAll(agent, _kvData);

            _debug.Write("CreatorAgentTool", "Create",
                $"Agent '{agent.Name}' created ({agent.Guid}).");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                agent_id = agent.Guid,
                name = agent.Name
            }));
        }

        private ToolResult UpdateAgent(JObject args)
        {
            if (!TryGetAgentId(args, out var agentId, out var idError))
                return idError;

            // 只应用参数里显式出现的字段； absent 与 null 语义不同（null = 清空）。
            // 类型不符立即失败，不做「当成 null」的静默修补。
            var mutations = new List<(string Field, Action<Agent> Apply)>();

            if (args.ContainsKey("name"))
            {
                if (!CreatorShared.TryGetStringArg(args, "name", out var name, out var nameError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, nameError!);
                if (string.IsNullOrWhiteSpace(name))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                        "'name' must not be empty.");
                mutations.Add(("name", a => a.Name = name!.Trim()));
            }

            if (args.ContainsKey("user_intro"))
            {
                if (!CreatorShared.TryGetStringArg(args, "user_intro", out var intro, out var introError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, introError!);
                mutations.Add(("user_intro", a => a.UserIntro = intro ?? ""));
            }

            if (args.ContainsKey("greeting"))
            {
                if (!CreatorShared.TryGetStringArg(args, "greeting", out var greeting, out var greetingError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, greetingError!);
                mutations.Add(("greeting", a => a.Greeting = greeting));
            }

            if (args.ContainsKey("alternative_greetings"))
            {
                if (!CreatorShared.TryGetStringArray(args, "alternative_greetings",
                        out var greetings, out var greetingsError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, greetingsError!);
                mutations.Add(("alternative_greetings", a => a.AlternativeGreetings = greetings!));
            }

            if (args.ContainsKey("tags"))
            {
                if (!CreatorShared.TryGetStringArray(args, "tags", out var tags, out var tagsError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, tagsError!);
                mutations.Add(("tags", a => a.Tags = tags!));
            }

            if (args.ContainsKey("chat_view_id"))
            {
                if (!CreatorShared.TryGetStringArg(args, "chat_view_id", out var viewId, out var viewIdError))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments, viewIdError!);
                if (viewId != null && IsUnknownChatView(viewId, out var viewNames))
                    return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                        $"Unknown chat_view_id '{viewId}'. Registered ids: {viewNames}. " +
                        "Use the list_chat_views action to inspect them.");
                mutations.Add(("chat_view_id", a => a.ChatViewId = viewId));
            }

            if (mutations.Count == 0)
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    "No updatable fields present. Provide at least one of: name, user_intro, greeting, " +
                    "alternative_greetings, tags, chat_view_id.");

            // Agent.Update 在进程内互斥下读改写，避免与其他电路的整对象 Save 相互覆盖。
            Agent updated;
            try
            {
                updated = Agent.Update(agentId, _kvData, agent =>
                {
                    foreach (var (_, apply) in mutations)
                        apply(agent);
                });
            }
            catch (InvalidOperationException)
            {
                return ToolResult.Fail(ToolErrorCodes.TargetNotFound,
                    $"No agent with id '{agentId}'.");
            }
            catch (JsonException ex)
            {
                return ToolResult.Fail(ToolErrorCodes.ServiceError,
                    $"The stored record of agent '{agentId}' is unreadable.", ex.ToString());
            }

            SyncLiveAgent(agentId, mutations);

            _debug.Write("CreatorAgentTool", "Update",
                $"Agent '{agentId}' fields updated: {string.Join(", ", mutations.Select(m => m.Field))}.");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                agent_id = agentId,
                updated_fields = mutations.Select(m => m.Field).ToArray()
            }));
        }

        private ToolResult DeleteAgent(JObject args)
        {
            if (!TryGetAgentId(args, out var agentId, out var idError))
                return idError;

            if (agentId == _liveAgent.Guid)
                return ToolResult.Fail(ToolErrorCodes.ProtectedTarget,
                    "This agent hosts the current chat session and cannot delete itself. " +
                    "Ask the user to delete it from the agent list page.");

            if (!TryLoadAgent(agentId, out var agent, out var failure))
                return failure;

            Agent.RemoveAgentFromAll(agent, _kvData);

            _debug.Write("CreatorAgentTool", "Delete",
                $"Agent '{agent.Name}' ({agentId}) removed from the registry; {agent.ChatGuids.Count} chat(s) left in storage.");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                deleted = true,
                agent_id = agentId,
                name = agent.Name,
                detached_chat_count = agent.ChatGuids.Count,
                note = "Removed from the agent registry. Chats and the stored record are kept, mirroring the UI delete."
            }));
        }

        // ------------------------------------------------------------------
        // 管线树
        // ------------------------------------------------------------------

        private ToolResult GetPipeline(JObject args)
        {
            if (!TryGetAgentId(args, out var agentId, out var idError))
                return idError;

            if (!TryGetPipeline(args, out var pipeline, out var pipelineError))
                return pipelineError;

            if (!TryLoadAgent(agentId, out var agent, out var failure))
                return failure;

            var treeJson = GetTree(agent, pipeline);
            if (string.IsNullOrWhiteSpace(treeJson))
            {
                return ToolResult.Ok(CreatorShared.Json(new
                {
                    agent_id = agentId,
                    pipeline = PipelineShortName(pipeline),
                    tree_json = (string?)null,
                    note = "No private tree configured; the global/default pipeline applies."
                }));
            }

            // 原样返回存储的树 JSON，模型可以精确编辑后经 set_pipeline 写回。
            return ToolResult.Ok(treeJson);
        }

        private ToolResult SetPipeline(JObject args)
        {
            if (!TryGetAgentId(args, out var agentId, out var idError))
                return idError;

            if (!TryGetPipeline(args, out var pipeline, out var pipelineError))
                return pipelineError;

            if (!args.ContainsKey("tree_json"))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    "'tree_json' is required (empty or '{}' clears the private tree).");

            // 类型不符必须显式失败：把数字/对象当成空字符串处理会静默清掉用户的树。
            if (!CreatorShared.TryGetStringArg(args, "tree_json", out var rawTree, out var treeJsonError))
                return ToolResult.Fail(ToolErrorCodes.InvalidArguments, treeJsonError!);

            string status;
            string? stored = null;
            bool migrated = false;

            if (string.IsNullOrWhiteSpace(rawTree) || rawTree!.Trim() == "{}")
            {
                status = "cleared";
            }
            else
            {
                var validation = CreatorShared.ValidateTree(
                    _services, pipeline, rawTree, out var normalized, out migrated);
                if (validation != null)
                    return validation;

                status = "updated";
                stored = normalized;
            }

            try
            {
                Agent.Update(agentId, _kvData, agent => SetTree(agent, pipeline, stored));
            }
            catch (InvalidOperationException)
            {
                return ToolResult.Fail(ToolErrorCodes.TargetNotFound,
                    $"No agent with id '{agentId}'.");
            }
            catch (JsonException ex)
            {
                return ToolResult.Fail(ToolErrorCodes.ServiceError,
                    $"The stored record of agent '{agentId}' is unreadable.", ex.ToString());
            }

            // 写穿到当前会话的活实例：宿主与 UI 共享同一 Agent 对象，
            // 不同步会让下一轮生成继续使用旧树，看起来像“改了没生效”。
            if (agentId == _liveAgent.Guid)
                SetTree(_liveAgent, pipeline, stored);

            _debug.Write("CreatorAgentTool", "SetPipeline",
                $"Agent '{agentId}' {pipeline} tree {status}" +
                (migrated ? " (migrated from legacy JSON format)" : "") + ".");

            return ToolResult.Ok(CreatorShared.Json(new
            {
                agent_id = agentId,
                pipeline = PipelineShortName(pipeline),
                status,
                migrated_from_legacy = migrated
            }));
        }

        // ------------------------------------------------------------------
        // 对话界面
        // ------------------------------------------------------------------

        private ToolResult ListChatViews()
        {
            var registry = _services?.GetService(typeof(IChatViewRegistry)) as IChatViewRegistry;
            if (registry == null)
                return ToolResult.Fail(ToolErrorCodes.HostServiceUnavailable,
                    "The chat view registry is not available in this context.");

            var views = registry.GetAll().Select(v => new
            {
                id = v.Id,
                name = _loc[v.NameKey],
                description = v.DescriptionKey != null ? _loc[v.DescriptionKey] : null,
                icon = v.Icon,
                is_default = v.IsDefault
            }).ToArray();

            return ToolResult.Ok(CreatorShared.Json(new { count = views.Length, views }));
        }

        // ------------------------------------------------------------------
        // 私有辅助
        // ------------------------------------------------------------------

        private void SyncLiveAgent(Guid agentId, List<(string Field, Action<Agent> Apply)> mutations)
        {
            if (agentId != _liveAgent.Guid)
                return;

            foreach (var (_, apply) in mutations)
                apply(_liveAgent);
        }

        private static string PipelineShortName(GenerationPipeline pipeline) => pipeline switch
        {
            GenerationPipeline.PreGeneration => "pre",
            GenerationPipeline.PostGeneration => "post",
            GenerationPipeline.RenderModifier => "render",
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private static string? GetTree(Agent agent, GenerationPipeline pipeline) => pipeline switch
        {
            GenerationPipeline.PreGeneration => agent.PreGenerationTreeJson,
            GenerationPipeline.PostGeneration => agent.PostGenerationTreeJson,
            GenerationPipeline.RenderModifier => agent.RenderModifierTreeJson,
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline."),
        };

        private static void SetTree(Agent agent, GenerationPipeline pipeline, string? treeJson)
        {
            switch (pipeline)
            {
                case GenerationPipeline.PreGeneration:
                    agent.PreGenerationTreeJson = treeJson;
                    break;
                case GenerationPipeline.PostGeneration:
                    agent.PostGenerationTreeJson = treeJson;
                    break;
                case GenerationPipeline.RenderModifier:
                    agent.RenderModifierTreeJson = treeJson;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pipeline), pipeline, "Unknown pipeline.");
            }
        }

        private object Summarize(Agent agent) => new
        {
            agent_id = agent.Guid,
            name = agent.Name,
            tags = agent.Tags,
            chat_view_id = agent.ChatViewId,
            chat_count = agent.ChatGuids.Count,
            pipelines = PipelineFlags(agent)
        };

        private static object PipelineFlags(Agent agent) => new
        {
            pre = !string.IsNullOrWhiteSpace(agent.PreGenerationTreeJson),
            post = !string.IsNullOrWhiteSpace(agent.PostGenerationTreeJson),
            render = !string.IsNullOrWhiteSpace(agent.RenderModifierTreeJson)
        };

        private bool TryLoadAgent(Guid guid, out Agent agent, out ToolResult failure)
        {
            try
            {
                agent = Agent.Load(guid, _kvData);
            }
            catch (InvalidOperationException)
            {
                agent = null!;
                failure = ToolResult.Fail(ToolErrorCodes.TargetNotFound,
                    $"No agent with id '{guid}'.");
                return false;
            }
            catch (Exception ex)
            {
                agent = null!;
                failure = ToolResult.Fail(ToolErrorCodes.ServiceError,
                    $"The stored record of agent '{guid}' is unreadable.", ex.ToString());
                return false;
            }

            failure = null!;
            return true;
        }

        private bool IsUnknownChatView(string viewId, out string knownIds)
        {
            var registry = _services?.GetService(typeof(IChatViewRegistry)) as IChatViewRegistry;
            var views = registry?.GetAll();
            if (views == null || views.Count == 0)
            {
                // 注册表不可用时无法校验：按原样接受，由宿主在显示期做显式回退 + 告警。
                knownIds = "";
                return false;
            }

            knownIds = string.Join(", ", views.Select(v => v.Id));
            return views.All(v => v.Id != viewId);
        }

        private static bool TryGetAgentId(JObject args, out Guid agentId, out ToolResult failure)
        {
            var raw = ReadString(args, "agent_id");
            if (!CreatorShared.TryParseGuid(raw, out agentId))
            {
                failure = ToolResult.Fail(ToolErrorCodes.InvalidArguments,
                    $"'agent_id' is required and must be a valid GUID, got: '{raw ?? "null"}'.");
                return false;
            }

            failure = null!;
            return true;
        }

        private static bool TryGetPipeline(JObject args, out GenerationPipeline pipeline, out ToolResult failure)
        {
            if (!CreatorShared.TryParsePipeline(ReadString(args, "pipeline"), out pipeline, out var error))
            {
                failure = ToolResult.Fail(ToolErrorCodes.InvalidArguments, error);
                return false;
            }

            failure = null!;
            return true;
        }

        private static string? ReadString(JObject args, string name)
        {
            if (!args.ContainsKey(name))
                return null;

            var token = args[name];
            if (token == null || token.Type == JTokenType.Null)
                return null;

            return token.Type == JTokenType.String ? token.Value<string>() : null;
        }
    }
}
