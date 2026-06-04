using ShimmerChatLib.Context;
using SharperLLM.Util;
using ShimmerChatLib;

namespace ShimmerChatLib.Interface
{
    public interface IContextModifierService
    {
        List<IContextModifier> LoadedModifiers { get; }
        List<ActivatedModifier> ActivatedModifiers { get; }
        List<ContextModifierPreset> Presets { get; }
        string ActivePresetId { get; }
        string ActivePresetName { get; }

        void ActivateModifier(string modifierName, ModifierConfig config);
        void RemoveActivatedModifier(int index);
        void ReorderActivatedModifier(int oldIndex, int newIndex);
        void ClearActivatedModifiers();
        void ApplyModifiers(ContextDocument context, Chat chat, Agent agent);
        void ApplyModifiers(PromptBuilder promptBuilder, Chat chat, Agent agent);
        void SavePresets();
        void CreatePreset(string name);
        void DeletePreset(string presetId);
        void RenamePreset(string presetId, string newName);
        void SwitchToPreset(string presetId);
    }

    public class ActivatedModifier
    {
        public required string Name { get; set; }
        public ModifierConfig? Config { get; set; }
        public bool IsEnabled { get; set; } = true;
    }

    public class ContextModifierPreset
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Default";
        public List<ActivatedModifier> Modifiers { get; set; } = new();
    }

    public class ContextModifierPresetCollection
    {
        public string ActivePresetId { get; set; } = "";
        public List<ContextModifierPreset> Presets { get; set; } = new();
    }
}
