using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib.Generation;
using ShimmerChatLib.Interface;
using ShimmerChatBuiltin.Variable;

namespace ShimmerChatBuiltin.Variable
{
    /// <summary>
    /// IToolV2 版本的 VariableTool。由 VariableToolNode 构造并注入依赖。
    /// </summary>
    public class VariableToolV2 : IToolV2
    {
        private readonly IKVDataService _kvData;
        private readonly Guid _chatGuid;
        private readonly Guid _agentGuid;

        public string Name => "VariableTool";
        public string Description => "Manage variables in conversations. Supports get, set, delete, list, search.";

        public VariableToolV2(IKVDataService kvData, Guid chatGuid, Guid agentGuid)
        {
            _kvData = kvData;
            _chatGuid = chatGuid;
            _agentGuid = agentGuid;
        }

        public Tool GetDefinition() => new()
        {
            name = "VariableTool",
            description = "A tool for managing variables. Supports: get, set, delete, list, search. Variables can be scoped to 'agent' or 'chat'.",
            parameters =
            [
                (new ToolParameter { name = "action", type = ParameterType.String, description = "Action: get, set, delete, list, search.", @enum = ["get", "set", "delete", "list", "search"] }, true),
                (new ToolParameter { name = "name", type = ParameterType.String, description = "Variable name." }, false),
                (new ToolParameter { name = "value", type = ParameterType.String, description = "Value to set." }, false),
                (new ToolParameter { name = "scope", type = ParameterType.String, description = "Scope: agent or chat.", @enum = ["agent", "chat"] }, false),
                (new ToolParameter { name = "type", type = ParameterType.String, description = "Data type: string, int, float.", @enum = ["string", "int", "float", "str", "integer"] }, false)
            ]
        };

        public Task<string> ExecuteAsync(string input)
        {
            var action = JsonConvert.DeserializeObject<VariableAction>(input);
            if (action == null) return Task.FromResult("Error: Invalid action format.");

            return action.Action.ToLowerInvariant() switch
            {
                "get" => Task.FromResult(HandleGet(action)),
                "set" => Task.FromResult(HandleSet(action)),
                "delete" => Task.FromResult(HandleDelete(action)),
                "list" => Task.FromResult(HandleList(action)),
                "search" => Task.FromResult(HandleSearch(action)),
                _ => Task.FromResult($"Error: Unknown action '{action.Action}'.")
            };
        }

        private string HandleGet(VariableAction action)
        {
            if (string.IsNullOrEmpty(action.Name)) return "Error: Variable name is required.";
            var v = VariableManager.GetVariableFromAggregated(_kvData, _chatGuid, _agentGuid, action.Name);
            if (v == null) return $"Variable '{action.Name}' not found.";
            return $"Variable: {v.Name}\nScope: {v.Scope}\nType: {v.Type}\nValue: {v.GetValueAsString()}";
        }

        private string HandleSet(VariableAction action)
        {
            if (string.IsNullOrEmpty(action.Name)) return "Error: Variable name is required.";
            if (string.IsNullOrEmpty(action.Value)) return "Error: Variable value is required.";

            var scope = action.Scope?.ToLowerInvariant() switch { "agent" => VariableScope.Agent, _ => VariableScope.Chat };
            var type = action.Type?.ToLowerInvariant() switch { "float" or "double" => VariableType.Float, "int" or "integer" => VariableType.Int, _ => VariableType.String };

            var value = ParseValue(action.Value, type);
            if (value == null) return $"Error: Cannot convert '{action.Value}' to {type}.";

            var variable = new Variable { Name = action.Name, Type = type, Scope = scope };
            variable.SetValue(value);

            if (scope == VariableScope.Agent) VariableManager.SetAgentVariable(_kvData, _agentGuid, variable);
            else VariableManager.SetChatVariable(_kvData, _chatGuid, variable);

            return $"Variable '{action.Name}' set successfully. Type: {type}, Value: {variable.GetValueAsString()}";
        }

        private string HandleDelete(VariableAction action)
        {
            if (string.IsNullOrEmpty(action.Name)) return "Error: Variable name is required.";
            var scope = action.Scope?.ToLowerInvariant() switch { "agent" => VariableScope.Agent, _ => VariableScope.Chat };
            if (scope == VariableScope.Agent) VariableManager.RemoveAgentVariable(_kvData, _agentGuid, action.Name);
            else VariableManager.RemoveChatVariable(_kvData, _chatGuid, action.Name);
            return $"Variable '{action.Name}' deleted.";
        }

        private string HandleList(VariableAction action)
        {
            var variables = VariableManager.GetAggregatedVariables(_kvData, _chatGuid, _agentGuid);
            var filtered = action.Scope?.ToLowerInvariant() switch
            {
                "agent" => variables.Variables.Where(v => v.Scope == VariableScope.Agent).ToList(),
                "chat" => variables.Variables.Where(v => v.Scope == VariableScope.Chat).ToList(),
                _ => variables.Variables
            };

            if (filtered.Count == 0) return "No variables found.";
            var sb = new StringBuilder();
            sb.AppendLine($"Found {filtered.Count} variable(s):\n");
            foreach (var v in filtered)
                sb.AppendLine($"{v.Scope} {v.Name} ({v.Type}) = {v.GetValueAsString()}");
            return sb.ToString();
        }

        private string HandleSearch(VariableAction action)
        {
            if (string.IsNullOrEmpty(action.Name)) return "Error: Search pattern is required.";
            var variables = VariableManager.GetAggregatedVariables(_kvData, _chatGuid, _agentGuid);
            var matched = variables.Variables.Where(v => v.Name.Contains(action.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matched.Count == 0) return $"No variables matching '{action.Name}'.";
            var sb = new StringBuilder();
            sb.AppendLine($"Found {matched.Count} variable(s) matching '{action.Name}':\n");
            foreach (var v in matched)
                sb.AppendLine($"{v.Scope} {v.Name} ({v.Type}) = {v.GetValueAsString()}");
            return sb.ToString();
        }

        private static object? ParseValue(string value, VariableType type) => type switch
        {
            VariableType.String => value,
            VariableType.Int => long.TryParse(value, out var i) ? i : null,
            VariableType.Float => double.TryParse(value, out var f) ? f : null,
            _ => null
        };
    }

    public class VariableAction
    {
        [Newtonsoft.Json.JsonProperty("action")]
        public string Action { get; set; } = string.Empty;

        [Newtonsoft.Json.JsonProperty("name")]
        public string? Name { get; set; }

        [Newtonsoft.Json.JsonProperty("value")]
        public string? Value { get; set; }

        [Newtonsoft.Json.JsonProperty("scope")]
        public string? Scope { get; set; }

        [Newtonsoft.Json.JsonProperty("type")]
        public string? Type { get; set; }
    }
}
