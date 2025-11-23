using ShimmerChatLib.Context;
using SharperLLM.Util;

namespace ShimmerChat.Singletons
{
    public interface IContextModifierService
    {
        /// <summary>
        /// 获取所有加载的ContextModifier
        /// </summary>
        List<IContextModifier> LoadedModifiers { get; }

        /// <summary>
        /// 获取所有激活的ContextModifier及其输入值
        /// </summary>
        List<ActivatedModifier> ActivatedModifiers { get; }

        /// <summary>
        /// 激活一个ContextModifier
        /// </summary>
        /// <param name="modifierName">ContextModifier的名称</param>
        /// <param name="inputValue">输入值</param>
        void ActivateModifier(string modifierName, string inputValue);

        /// <summary>
        /// 移除指定索引的激活的ContextModifier
        /// </summary>
        /// <param name="index">要移除的索引</param>
        void RemoveActivatedModifier(int index);

        /// <summary>
        /// 清除所有激活的ContextModifier
        /// </summary>
        void ClearActivatedModifiers();

        /// <summary>
        /// 顺序应用所有激活的ContextModifier到PromptBuilder
        /// </summary>
        /// <param name="promptBuilder">要修改的PromptBuilder</param>
        /// <param name="input">用户输入</param>
        void ApplyModifiers(PromptBuilder promptBuilder, string input);

        public void SaveActivatedModifiers();
	}

    /// <summary>
    /// 激活的ContextModifier及其输入值
    /// </summary>
    public class ActivatedModifier
    {
        public required string Name { get; set; }
        public required string Value { get; set; }
    }
}