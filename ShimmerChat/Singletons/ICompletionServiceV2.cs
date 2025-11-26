using SharperLLM.API;
using SharperLLM.Util;
using SharperLLM.FunctionCalling;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerChat.Singletons
{
    public interface ICompletionServiceV2
    {
        /// <summary>
        /// 生成文本回复
        /// </summary>
        /// <param name="promptBuilder">提示构建器</param>
        /// <param name="sysSeqPrefix">系统序列前缀</param>
        /// <param name="sysSeqSuffix">系统序列后缀</param>
        /// <param name="inputPrefix">输入前缀</param>
        /// <param name="inputSuffix">输入后缀</param>
        /// <param name="outputPrefix">输出前缀</param>
        /// <param name="outputSuffix">输出后缀</param>
        /// <returns>生成的文本</returns>
        Task<string> GenerateTextAsync(
            PromptBuilder promptBuilder,
            string sysSeqPrefix,
            string sysSeqSuffix,
            string inputPrefix,
            string inputSuffix,
            string outputPrefix,
            string outputSuffix);
        
        /// <summary>
        /// 生成聊天回复
        /// </summary>
        /// <param name="promptBuilder">提示构建器</param>
        /// <returns>生成的回复字符串</returns>
        Task<string> GenerateChatReplyAsync(PromptBuilder promptBuilder);
        
        /// <summary>
        /// 生成增强的聊天回复（包含工具调用信息）
        /// </summary>
        /// <param name="promptBuilder">提示构建器</param>
        /// <returns>增强的回复对象</returns>
        Task<ResponseEx> GenerateChatExAsync(PromptBuilder promptBuilder);
        
        /// <summary>
        /// 流式生成增强的聊天回复
        /// </summary>
        /// <param name="promptBuilder">提示构建器</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>增强的回复对象的异步流</returns>
        IAsyncEnumerable<ResponseEx> GenerateChatExStreamAsync(PromptBuilder promptBuilder, CancellationToken cancellationToken);
    }
}