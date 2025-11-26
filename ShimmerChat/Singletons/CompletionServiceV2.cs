using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ShimmerChat.Singletons
{
    public class CompletionServiceV2 : ICompletionServiceV2
    {
        private readonly IUserData _userData;

        public CompletionServiceV2(IUserData userData)
        {
            _userData = userData;
        }

        public async Task<string> GenerateTextAsync(
            PromptBuilder promptBuilder,
            string sysSeqPrefix,
            string sysSeqSuffix,
            string inputPrefix,
            string inputSuffix,
            string outputPrefix,
            string outputSuffix)
        {
            var finalPromptBuilder = new PromptBuilder(promptBuilder)
            {
                SysSeqPrefix = sysSeqPrefix,
                SysSeqSuffix = sysSeqSuffix,
                InputPrefix = inputPrefix,
                InputSuffix = inputSuffix,
                OutputPrefix = outputPrefix,
                OutputSuffix = outputSuffix,
            };
            
            return await _userData.ApiSettings[_userData.CurrentAPISettingIndex].llmapi
                .GenerateText(finalPromptBuilder.GeneratePromptWithLatestOuputPrefix());
        }

        public async Task<string> GenerateChatReplyAsync(PromptBuilder promptBuilder)
        {
            return await _userData.ApiSettings[_userData.CurrentAPISettingIndex].llmapi
                .GenerateChatReply(promptBuilder);
        }

        public async Task<ResponseEx> GenerateChatExAsync(PromptBuilder promptBuilder)
        {
            return await _userData.ApiSettings[_userData.CurrentAPISettingIndex].llmapi
                .GenerateChatEx(promptBuilder);
        }
        
        public async IAsyncEnumerable<ResponseEx> GenerateChatExStreamAsync(PromptBuilder promptBuilder, CancellationToken cancellationToken)
        {
            var apiSetting = _userData.ApiSettings[_userData.CurrentAPISettingIndex];
            
            // 对于OpenAI类型的API，根据OpenAIStream属性决定是否使用流式调用
            if (apiSetting.Type == ApiSettingType.OpenAI && apiSetting.OpenAIStream)
            {
                // 使用流式API
                await foreach (var response in apiSetting.llmapi.GenerateChatExStream(promptBuilder, cancellationToken))
                {
                    yield return response;
                }
            }
            else
            {
                // 对于非OpenAI类型或未启用流式的情况，使用非流式API并模拟流式输出
                var response = await apiSetting.llmapi.GenerateChatEx(promptBuilder);
                yield return response;
            }
        }
    }
}