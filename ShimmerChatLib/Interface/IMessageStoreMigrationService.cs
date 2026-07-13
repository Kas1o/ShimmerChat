namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// 消息存储双向迁移服务接口。
    /// </summary>
    public interface IMessageStoreMigrationService
    {
        int MigrateFileToLite();
        int MigrateLiteToFile();
        int GetFileStoreCount();
        int GetLiteStoreCount();
    }
}
