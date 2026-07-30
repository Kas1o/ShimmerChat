using Newtonsoft.Json;
using ShimmerChatLib.Generation;
using System;
using System.Collections.Generic;
using System.Text;

namespace ShimmerChatBuiltin.NodeBasic.PreGeneration
{
	/// <summary>
	/// 回退执行节点，
	/// 提供节点列表，从上往下执行到节点执行正确为止。
	/// 通常可搭配 <see cref="SequenceNode"/> 使用。
	/// </summary>
	[NodeInfo("node.fallback_execute")]
	public class FallbackExecuteNode : IPreGenerationNode
	{
		public string Id { get; } = Guid.NewGuid().ToString();

		public string Name { get; set; } = nameof(FallbackExecuteNode);

		[NodeProperty("node.fallback_execute.fallback_list")]
		public List<IPreGenerationNode>? FallbackList { get; set; } = new();

		public async Task<NodeResult> ExecuteAsync(PreNodeExecutionContext context)
		{
			if (FallbackList == null || FallbackList.Count == 0)
				return NodeResult.Failure(NodeErrorCodes.ParameterError, "Fallback list empty or null!");

			var errorResults = new List<NodeResult>();

			foreach (var node in FallbackList) 
			{
				var result = await node.ExecuteAsync(context);
				if (result.Success)
				{
					return result;
				}
				errorResults.Add(result);
			}

			return NodeResult.Failure(NodeErrorCodes.CommonError, "All Fallback Nodes Execute Failed." + JsonConvert.SerializeObject(errorResults));
		}
	}
}
