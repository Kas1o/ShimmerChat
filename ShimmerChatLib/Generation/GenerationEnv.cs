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
}
