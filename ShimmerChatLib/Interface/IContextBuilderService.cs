using SharperLLM.Util;
using SharperLLM.FunctionCalling;
using ShimmerChatLib;
using ShimmerChatLib.Context;
using System.Collections.Generic;

namespace ShimmerChatLib.Interface
{
    public interface IContextBuilderService
    {
        ContextDocument BuildContextDocument(Chat chat, Agent agent);
        ContextDocument BuildContextDocumentWithTools(Chat chat, Agent agent, List<SharperLLM.FunctionCalling.Tool> toolDefinitions);

        PromptBuilder BuildPromptBuilder(Chat chat, Agent agent);
        PromptBuilder BuildPromptBuilderWithTools(Chat chat, Agent agent, List<SharperLLM.FunctionCalling.Tool> toolDefinitions);
        PromptBuilder BuildPromptBuilderWithoutContextModify(Chat chat, Agent agent);
        PromptBuilder BuildPromptBuilderForContinuation(Chat chat, Agent agent, List<SharperLLM.FunctionCalling.Tool> toolDefinitions, Message continuationMessage);
    }
}