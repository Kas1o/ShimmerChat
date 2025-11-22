using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;

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
    }
}