using SharperLLM.API;
using SharperLLM.Util;
using ShimmerChatLib;
using ShimmerChatLib.Interface;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ShimmerChat.Singletons
{
    public class CompletionServiceV2 : ICompletionServiceV2
    {
		private readonly IKVDataService KVDataService;
		public CompletionServiceV2(IKVDataService kVData)
        {
			KVDataService = kVData;
		}

		List<ApiSetting> ApiSettings
		{
			get
			{
				var apisettings = KVDataService.Read("ApiSettings", "apiSetting") ?? "null";
				return Newtonsoft.Json.JsonConvert.DeserializeObject<List<ApiSetting>>(apisettings);
			}
		}

		int SelectedAPIIndex
		{
			get
			{
				var selectedIndex = KVDataService.Read("ApiSettings", "selectedAPIIndex") ?? "0";
				return int.Parse(selectedIndex);
			}
		}

		public async IAsyncEnumerable<string> GenerateTextStreamAsync(
            PromptBuilder promptBuilder,
            string sysSeqPrefix,
            string sysSeqSuffix,
            string inputPrefix,
            string inputSuffix,
            string outputPrefix,
            string outputSuffix,
            CancellationToken cancellationToken)
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
            
            await foreach (var text in ApiSettings[SelectedAPIIndex].LLMApi
                .GenerateTextStream(finalPromptBuilder.GeneratePromptWithLatestOuputPrefix(), cancellationToken))
            {
                yield return text;
            }
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
            
            return await ApiSettings[SelectedAPIIndex].LLMApi
                .GenerateText(finalPromptBuilder.GeneratePromptWithLatestOuputPrefix());
        }

        public async Task<string> GenerateChatReplyAsync(PromptBuilder promptBuilder)
        {
            return await ApiSettings[SelectedAPIIndex].LLMApi
                .GenerateChatReply(promptBuilder);
        }

        public async Task<ResponseEx> GenerateChatExAsync(PromptBuilder promptBuilder)
        {
            return await ApiSettings[SelectedAPIIndex].LLMApi
                .GenerateChatEx(promptBuilder);
        }
        
        public async IAsyncEnumerable<ResponseEx> GenerateChatExStreamAsync(PromptBuilder promptBuilder, CancellationToken cancellationToken)
        {
            var apiSetting = ApiSettings[SelectedAPIIndex];
            
            // 对于OpenAI类型的API，根据OpenAIStream属性决定是否使用流式调用
            if (apiSetting.Type == ApiSettingType.OpenAI && apiSetting.OpenAIStream)
            {
                // 使用流式API
                await foreach (var response in apiSetting.LLMApi.GenerateChatExStream(promptBuilder, cancellationToken))
                {
                    yield return response;
                }
            }
            else
            {
                // 对于非OpenAI类型或未启用流式的情况，使用非流式API并模拟流式输出
                var response = await apiSetting.LLMApi.GenerateChatEx(promptBuilder);
                yield return response;
            }
        }
    }
}