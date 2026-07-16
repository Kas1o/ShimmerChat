using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Variable;

public class VariableInjectNode : IPreGenerationNode
{
    public string Id {get;set;} = Guid.NewGuid().ToString();

    public string Name {get; set;}

    // TODO: 添加属性、分别管理添加对话变量 or 添加Agent变量。
    
    public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
    {
        // 获取变量
        var persistentEnv = context.Env.Persistent;
        var variables = VariableManager.GetAggregatedVariables(persistentEnv.KVData,persistentEnv.ChatGuid, persistentEnv.AgentGuid);
        


        // 变量映射到字符串
        var strings = variables.Variables.Select(x =>{
            var value = x.GetValue().ToString();
            var key = x.Name;
            return $"{key}: {value}";
        });

        // 添加消息
        context.Env.Transient.Fragments.Add(new ContextSegment {
            Message =  String.Join("\n", strings),
            From = SharperLLM.Util.PromptBuilder.From.system,
            SourceType = typeof(VariableInjectNode)
        });

        return NodeResult.SuccessResult();
    }
}