using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib
{
    public class PseudoChatCompletionClient : IChatCompletionClient
    {
        public IAsyncEnumerable<ResponseEx> GenerateStreamAsync(PromptBuilder pb, CancellationToken cancellationToken)
        {
            var content = GetLastMessageContent(pb);
            var response = new ResponseEx
            {
                Body = new ChatMessage { Content = content },
                FinishReason = FinishReason.Stop
            };
            return Yield(response);
        }

        public Task<ResponseEx> GenerateAsync(PromptBuilder pb)
        {
            var content = GetLastMessageContent(pb);
            var response = new ResponseEx
            {
                Body = new ChatMessage { Content = content },
                FinishReason = FinishReason.Stop
            };
            return Task.FromResult(response);
        }

        private static string GetLastMessageContent(PromptBuilder promptBuilder)
        {
            if (promptBuilder.Messages.Length > 0)
            {
                return promptBuilder.Messages[^1].Item1.Content;
            }
            return string.Empty;
        }

        private static async IAsyncEnumerable<T> Yield<T>(T value)
        {
            yield return value;
            await Task.CompletedTask;
        }
    }
}
