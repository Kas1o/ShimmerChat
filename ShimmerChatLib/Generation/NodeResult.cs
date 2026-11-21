namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 节点执行统一结果结构。
    /// Success=true 表示成功；Success=false 时可通过 Code/Message/Details 获取失败信息。
    /// </summary>
    public class NodeResult
    {
        /// <summary>是否执行成功</summary>
        public bool Success { get; set; }

        /// <summary>错误码，如 PRESET_NOT_FOUND、TOOL_NOT_FOUND、API_UNAVAILABLE 等</summary>
        public string? Code { get; set; }

        /// <summary>用户可读的错误信息</summary>
        public string? Message { get; set; }

        /// <summary>详细的错误技术信息（可展开）</summary>
        public string? Details { get; set; }

        /// <summary>产生此结果的节点 ID</summary>
        public string? NodeId { get; set; }

        /// <summary>产生此结果的节点名称</summary>
        public string? NodeName { get; set; }

        public static NodeResult SuccessResult()
        {
            return new NodeResult { Success = true };
        }

        /// <summary>
        /// 创建针对指定节点的失败结果
        /// </summary>
        public static NodeResult Failure(string code, string message, string? details = null, string? nodeId = null, string? nodeName = null)
        {
            return new NodeResult
            {
                Success = false,
                Code = code,
                Message = message,
                Details = details,
                NodeId = nodeId,
                NodeName = nodeName
            };
        }
    }

    /// <summary>
    /// 预定义通用错误码常量
    /// </summary>
    public static class NodeErrorCodes
    {
        /// <summary>Preset 未找到</summary>
        public const string PresetNotFound = "PRESET_NOT_FOUND";

        /// <summary>工具类型解析/实例化失败</summary>
        public const string ToolNotFound = "TOOL_NOT_FOUND";

        /// <summary>API 配置不可用</summary>
        public const string ApiUnavailable = "API_UNAVAILABLE";

        /// <summary>KVData 读取失败或数据缺失</summary>
        public const string DataMissing = "DATA_MISSING";

        /// <summary>表达式/参数解析失败</summary>
        public const string ParseError = "PARSE_ERROR";

        /// <summary>外部服务调用失败（数据库、向量检索、网络等）</summary>
        public const string ServiceError = "SERVICE_ERROR";

        /// <summary>配置未找到</summary>
        public const string ConfigNotFound = "CONFIG_NOT_FOUND";

        /// <summary>执行被取消</summary>
        public const string Cancelled = "CANCELLED";

		/// <summary> 参数错误 </summary>
		public const string ParameterError = "PARAM_ERROR";

        /// <summary> 通常错误 </summary>
        public const string CommonError = "COMMON_ERROR";

        /// <summary> 包装器缺少业务节点负载（反序列化失败或数据损坏） </summary>
        public const string PayloadMissing = "NODE_PAYLOAD_MISSING";

        /// <summary> 需要注入槽的节点找不到带标记的注入槽 </summary>
        public const string InjectionSlotMissing = "INJECTION_SLOT_MISSING";

        /// <summary> 找到多个带标记的注入槽，注入位置不唯一 </summary>
        public const string InjectionSlotAmbiguous = "INJECTION_SLOT_AMBIGUOUS";
    }
}
