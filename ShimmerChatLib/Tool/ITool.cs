using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShimmerChatLib.Tool
{
    public interface ITool
    {
        public Task<string> Execute(string input);
        public SharperLLM.FunctionCalling.Tool GetToolDefinition();
    }
}
