using SharperLLM.API;
using SharperLLM.Util;

namespace ShimmerChatLib
{
    public class PseudoAPI : ILLMAPI
    {
        public IAsyncEnumerable<string> GenerateTextStream(string prompt, CancellationToken cancellationToken)
        {
            return Yield(prompt);
        }

        public Task<string> GenerateText(string prompt, int retry = 0)
        {
            return Task.FromResult(prompt);
        }

        public IAsyncEnumerable<string> GenerateChatReplyStream(PromptBuilder promptBuilder, CancellationToken cancellationToken)
        {
            var lastMessage = GetLastMessageContent(promptBuilder);
            return Yield(lastMessage);
        }

        public Task<string> GenerateChatReply(PromptBuilder promptBuilder)
        {
            return Task.FromResult(GetLastMessageContent(promptBuilder));
        }

        public IAsyncEnumerable<ResponseEx> GenerateChatExStream(PromptBuilder pb, CancellationToken cancellationToken)
        {
            var content = GetLastMessageContent(pb);
            var response = new ResponseEx
            {
                Body = new ChatMessage { Content = content },
                FinishReason = FinishReason.Stop
            };
            return Yield(response);
        }

        public Task<ResponseEx> GenerateChatEx(PromptBuilder pb)
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
