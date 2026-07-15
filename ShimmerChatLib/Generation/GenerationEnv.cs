using System;

namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 完整的预生成环境，组合 TransientEnv 和 PersistentEnv
    /// </summary>
    public class PreGenerationEnv
    {
        public TransientEnv Transient { get; set; } = new();
        public PersistentEnv Persistent { get; set; }

        public PreGenerationEnv(PersistentEnv persistent)
        {
            Persistent = persistent;
        }
    }

    /// <summary>
    /// 后向兼容：GenerationEnv 已重命名为 PreGenerationEnv。
    /// </summary>
    [Obsolete("Use PreGenerationEnv instead")]
    public class GenerationEnv : PreGenerationEnv
    {
        public GenerationEnv(PersistentEnv persistent) : base(persistent) { }
    }
}
