using ShimmerChatLib.Generation;

namespace ShimmerChatBuiltin.Variable;

[NodeInfo("node.variableinject", Icon = "💉", Color = "var(--node-prompt)", CategoryKeys = ["category.content", "category.fragment"], DescriptionKey = "node.variableinject.desc")]
public class VariableInjectNode : IPreGenerationNode
{
    public string Id {get;set;} = Guid.NewGuid().ToString();

    public string Name {get; set;}

    [NodeProperty("node.variableinject.template", MultiLine =true)]
    public string template {get;set;} = "<inject_variables>{0}</inject_variables>";

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
            Message =  string.Format(template, String.Join("\n", strings)),
            From = SharperLLM.Util.PromptBuilder.From.system,
            SourceType = typeof(VariableInjectNode)
        });

        return NodeResult.SuccessResult();
    }
}