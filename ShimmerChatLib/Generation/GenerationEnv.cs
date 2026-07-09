namespace ShimmerChatLib.Generation
{
    /// <summary>
    /// 完整的生成环境，组合 TransientEnv 和 PersistentEnv
    /// </summary>
    public class GenerationEnv
    {
        public TransientEnv Transient { get; set; } = new();
        public PersistentEnv Persistent { get; set; }

        public GenerationEnv(PersistentEnv persistent)
        {
            Persistent = persistent;
        }
    }
}
