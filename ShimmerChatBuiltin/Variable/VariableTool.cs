using System.Text;
using Newtonsoft.Json;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using ShimmerChatLib.Tool;
using ShimmerChatBuiltin.Variable;

namespace ShimmerChatBuiltin.Variable
{
    public class VariableTool : ITool
    {
        private readonly IKVDataService _kvData;

        public VariableTool(IKVDataService kvData)
        {
            _kvData = kvData;
        }

        async Task<string> ITool.Execute(string input, Chat? chat, Agent? agent)
        {
            if (chat == null || agent == null)
            {
                return "Error: Chat or Agent context is missing.";
            }

            var action = JsonConvert.DeserializeObject<VariableAction>(input);
            if (action == null)
            {
                return "Error: Invalid action format.";
            }

            return action.Action.ToLowerInvariant() switch
            {
                "get" => await HandleGet(action, chat, agent),
                "set" => await HandleSet(action, chat, agent),
                "delete" => await HandleDelete(action, chat, agent),
                "list" => await HandleList(action, chat, agent),
                "search" => await HandleSearch(action, chat, agent),
                _ => $"Error: Unknown action '{action.Action}'. Supported actions: get, set, delete, list, search."
            };
        }

        private Task<string> HandleGet(VariableAction action, Chat chat, Agent agent)
        {
            if (string.IsNullOrEmpty(action.Name))
            {
                return Task.FromResult("Error: Variable name is required for 'get' action.");
            }

            var variable = VariableManager.GetVariableFromAggregated(_kvData, chat.Guid, agent.guid, action.Name);
            if (variable == null)
            {
                return Task.FromResult($"Variable '{action.Name}' not found.");
            }

            var scopeStr = variable.Scope == VariableScope.Agent ? "Agent" : "Chat";
            return Task.FromResult($"Variable: {variable.Name}\nScope: {scopeStr}\nType: {variable.Type}\nValue: {variable.GetValueAsString()}\nCreated: {variable.CreatedAt:yyyy-MM-dd HH:mm:ss}\nUpdated: {variable.UpdatedAt:yyyy-MM-dd HH:mm:ss}");
        }

        private Task<string> HandleSet(VariableAction action, Chat chat, Agent agent)
        {
            if (string.IsNullOrEmpty(action.Name))
            {
                return Task.FromResult("Error: Variable name is required for 'set' action.");
            }

            if (string.IsNullOrEmpty(action.Value))
            {
                return Task.FromResult("Error: Variable value is required for 'set' action.");
            }

            var scope = action.Scope?.ToLowerInvariant() switch
            {
                "agent" => VariableScope.Agent,
                "chat" => VariableScope.Chat,
                _ => VariableScope.Chat
            };

            var type = action.Type?.ToLowerInvariant() switch
            {
                "float" or "double" => VariableType.Float,
                "int" or "integer" => VariableType.Int,
                "string" or "str" => VariableType.String,
                _ => VariableType.String
            };

            var value = ParseValue(action.Value, type);
            if (value == null)
            {
                return Task.FromResult($"Error: Cannot convert value '{action.Value}' to type {type}.");
            }

            var variable = new Variable
            {
                Name = action.Name,
                Type = type,
                Scope = scope
            };
            variable.SetValue(value);

            if (scope == VariableScope.Agent)
            {
                VariableManager.SetAgentVariable(_kvData, agent.guid, variable);
            }
            else
            {
                VariableManager.SetChatVariable(_kvData, chat.Guid, variable);
            }

            var scopeStr = scope == VariableScope.Agent ? "Agent" : "Chat";
            return Task.FromResult($"Variable '{action.Name}' set successfully in {scopeStr} scope. Type: {type}, Value: {variable.GetValueAsString()}");
        }

        private Task<string> HandleDelete(VariableAction action, Chat chat, Agent agent)
        {
            if (string.IsNullOrEmpty(action.Name))
            {
                return Task.FromResult("Error: Variable name is required for 'delete' action.");
            }

            var scope = action.Scope?.ToLowerInvariant() switch
            {
                "agent" => VariableScope.Agent,
                "chat" => VariableScope.Chat,
                _ => VariableScope.Chat
            };

            if (scope == VariableScope.Agent)
            {
                VariableManager.RemoveAgentVariable(_kvData, agent.guid, action.Name);
            }
            else
            {
                VariableManager.RemoveChatVariable(_kvData, chat.Guid, action.Name);
            }

            var scopeStr = scope == VariableScope.Agent ? "Agent" : "Chat";
            return Task.FromResult($"Variable '{action.Name}' deleted from {scopeStr} scope.");
        }

        private Task<string> HandleList(VariableAction action, Chat chat, Agent agent)
        {
            var variables = VariableManager.GetAggregatedVariables(_kvData, chat.Guid, agent.guid);
            var filteredVariables = variables.Variables;

            if (!string.IsNullOrEmpty(action.Scope))
            {
                var filterScope = action.Scope.ToLowerInvariant() switch
                {
                    "agent" => VariableScope.Agent,
                    "chat" => VariableScope.Chat,
                    _ => VariableScope.Chat
                };
                filteredVariables = filteredVariables.Where(v => v.Scope == filterScope).ToList();
            }

            if (filteredVariables.Count == 0)
            {
                return Task.FromResult("No variables found.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {filteredVariables.Count} variable(s):\n");

            foreach (var v in filteredVariables)
            {
                var scopeStr = v.Scope == VariableScope.Agent ? "[Agent]" : "[Chat]";
                sb.AppendLine($"{scopeStr} {v.Name} ({v.Type}) = {v.GetValueAsString()}");
            }

            return Task.FromResult(sb.ToString());
        }

        private Task<string> HandleSearch(VariableAction action, Chat chat, Agent agent)
        {
            if (string.IsNullOrEmpty(action.Name))
            {
                return Task.FromResult("Error: Search pattern is required for 'search' action.");
            }

            var variables = VariableManager.GetAggregatedVariables(_kvData, chat.Guid, agent.guid);
            var matchedVariables = variables.Variables
                .Where(v => v.Name.Contains(action.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchedVariables.Count == 0)
            {
                return Task.FromResult($"No variables found matching pattern '{action.Name}'.");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {matchedVariables.Count} variable(s) matching '{action.Name}':\n");

            foreach (var v in matchedVariables)
            {
                var scopeStr = v.Scope == VariableScope.Agent ? "[Agent]" : "[Chat]";
                sb.AppendLine($"{scopeStr} {v.Name} ({v.Type}) = {v.GetValueAsString()}");
            }

            return Task.FromResult(sb.ToString());
        }

        private static object? ParseValue(string value, VariableType type)
        {
            return type switch
            {
                VariableType.String => value,
                VariableType.Int => long.TryParse(value, out var i) ? i : null,
                VariableType.Float => double.TryParse(value, out var f) ? f : null,
                _ => null
            };
        }

        Tool ITool.GetToolDefinition() => new Tool
        {
            name = "VariableTool",
            description = "A tool for managing variables in conversations. Supports operations: get (retrieve variable value), set (create or update variable), delete (remove variable), list (list all variables), and search (search variables by name pattern). Variables can be scoped to either 'agent' (persistent across all chats with the agent) or 'chat' (specific to current conversation).",
            parameters = new List<(ToolParameter, bool)>
            {
                (new ToolParameter
                {
                    name = "action",
                    type = ParameterType.String,
                    description = "The action to perform: get, set, delete, list, or search.",
                    @enum = ["get", "set", "delete", "list", "search"]
                }, true),
                (new ToolParameter
                {
                    name = "name",
                    type = ParameterType.String,
                    description = "The name of the variable. Required for get, set, delete, and search actions."
                }, false),
                (new ToolParameter
                {
                    name = "value",
                    type = ParameterType.String,
                    description = "The value to set for the variable. Required for set action. Can be string, number (for int/float types)."
                }, false),
                (new ToolParameter
                {
                    name = "scope",
                    type = ParameterType.String,
                    description = "The scope of the variable: 'agent' (persistent across all chats) or 'chat' (specific to current conversation). Default: 'chat'.",
                    @enum = ["agent", "chat"]
                }, false),
                (new ToolParameter
                {
                    name = "type",
                    type = ParameterType.String,
                    description = "The data type of the variable: 'string', 'int', or 'float'. Default: 'string'.",
                    @enum = ["string", "int", "float", "str", "integer"]
                }, false)
            }
        };
    }

    public class VariableAction
    {
        [JsonProperty("action")]
        public string Action { get; set; } = string.Empty;

        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("value")]
        public string? Value { get; set; }

        [JsonProperty("scope")]
        public string? Scope { get; set; }

        [JsonProperty("type")]
        public string? Type { get; set; }
    }
}
