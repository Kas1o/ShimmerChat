namespace ShimmerChatLib.Generation
{
    public interface IGenerationNodeSerializer
    {
        string Serialize(IGenerationNode root);
        IGenerationNode? Deserialize(string json);
        IReadOnlyDictionary<string, Type> GetKnownTypes();
    }
}
