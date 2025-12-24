using Newtonsoft.Json;
using ShimmerChatLib;
using ShimmerChatLib.Interface;

namespace ShimmerChatBuiltin.Variable
{
    public static class VariableManager
    {
        private const string AgentSpacePrefix = "AgentVariables";
        private const string ChatSpacePrefix = "ChatVariables";

        public static VariableSet GetAgentVariables(IKVDataService kvData, Guid agentGuid)
        {
            var json = kvData.Read(AgentSpacePrefix, agentGuid.ToString());
            return DeserializeVariables(json) ?? new VariableSet();
        }

        public static VariableSet GetChatVariables(IKVDataService kvData, Guid chatGuid)
        {
            var json = kvData.Read(ChatSpacePrefix, chatGuid.ToString());
            return DeserializeVariables(json) ?? new VariableSet();
        }

        public static VariableSet GetAggregatedVariables(IKVDataService kvData, Guid chatGuid, Guid agentGuid)
        {
            var result = new VariableSet();

            var agentVariables = GetAgentVariables(kvData, agentGuid);
            var chatVariables = GetChatVariables(kvData, chatGuid);

            foreach (var variable in agentVariables.Variables)
            {
                result.Variables.Add(new Variable
                {
                    Name = variable.Name,
                    Type = variable.Type,
                    StringValue = variable.StringValue,
                    FloatValue = variable.FloatValue,
                    IntValue = variable.IntValue,
                    CreatedAt = variable.CreatedAt,
                    UpdatedAt = variable.UpdatedAt,
                    Scope = VariableScope.Agent
                });
            }

            foreach (var variable in chatVariables.Variables)
            {
                var existing = result.GetVariable(variable.Name);
                if (existing != null)
                {
                    var index = result.Variables.IndexOf(existing);
                    result.Variables[index] = new Variable
                    {
                        Name = variable.Name,
                        Type = variable.Type,
                        StringValue = variable.StringValue,
                        FloatValue = variable.FloatValue,
                        IntValue = variable.IntValue,
                        CreatedAt = variable.CreatedAt,
                        UpdatedAt = variable.UpdatedAt,
                        Scope = VariableScope.Chat
                    };
                }
                else
                {
                    result.Variables.Add(new Variable
                    {
                        Name = variable.Name,
                        Type = variable.Type,
                        StringValue = variable.StringValue,
                        FloatValue = variable.FloatValue,
                        IntValue = variable.IntValue,
                        CreatedAt = variable.CreatedAt,
                        UpdatedAt = variable.UpdatedAt,
                        Scope = VariableScope.Chat
                    });
                }
            }

            return result;
        }

        public static void SetAgentVariable(IKVDataService kvData, Guid agentGuid, Variable variable)
        {
            var variables = GetAgentVariables(kvData, agentGuid);
            variables.SetVariable(variable);
            var json = SerializeVariables(variables);
            kvData.Write(AgentSpacePrefix, agentGuid.ToString(), json);
        }

        public static void SetChatVariable(IKVDataService kvData, Guid chatGuid, Variable variable)
        {
            var variables = GetChatVariables(kvData, chatGuid);
            variables.SetVariable(variable);
            var json = SerializeVariables(variables);
            kvData.Write(ChatSpacePrefix, chatGuid.ToString(), json);
        }

        public static void RemoveAgentVariable(IKVDataService kvData, Guid agentGuid, string variableName)
        {
            var variables = GetAgentVariables(kvData, agentGuid);
            variables.RemoveVariable(variableName);
            var json = SerializeVariables(variables);
            kvData.Write(AgentSpacePrefix, agentGuid.ToString(), json);
        }

        public static void RemoveChatVariable(IKVDataService kvData, Guid chatGuid, string variableName)
        {
            var variables = GetChatVariables(kvData, chatGuid);
            variables.RemoveVariable(variableName);
            var json = SerializeVariables(variables);
            kvData.Write(AgentSpacePrefix, chatGuid.ToString(), json);
        }

        public static Variable? GetAgentVariable(IKVDataService kvData, Guid agentGuid, string variableName)
        {
            var variables = GetAgentVariables(kvData, agentGuid);
            return variables.GetVariable(variableName);
        }

        public static Variable? GetChatVariable(IKVDataService kvData, Guid chatGuid, string variableName)
        {
            var variables = GetChatVariables(kvData, chatGuid);
            return variables.GetVariable(variableName);
        }

        public static Variable? GetVariableFromAggregated(IKVDataService kvData, Guid chatGuid, Guid agentGuid, string variableName)
        {
            var variables = GetAggregatedVariables(kvData, chatGuid, agentGuid);
            return variables.GetVariable(variableName);
        }

        public static string SerializeVariables(VariableSet variables)
        {
            return JsonConvert.SerializeObject(variables, Formatting.Indented);
        }

        public static VariableSet? DeserializeVariables(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return null;
            try
            {
                return JsonConvert.DeserializeObject<VariableSet>(json);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"[VariableManager] Error deserializing variables: {ex.Message}");
                return null;
            }
        }
    }
}
