namespace ShimmerChatLib.Interface
{
    /// <summary>
    /// KV 数据迁移服务接口。
    /// </summary>
    public interface IKVDataMigrationService
    {
        int MigrateToLiteDB(bool clearSource = false);
        int MigrateToLocalFileStorage(bool clearSource = false);
        int SyncStorages();
        int GetLocalFileStorageCount();
        int GetLiteDBCount();
    }
}
