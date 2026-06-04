using System;
using System.Collections.Generic;

namespace ShimmerChatLib.Interface
{
    public interface IMessageStoreService
    {
        void InsertMessage(Guid chatGuid, Message message);
        void UpdateMessage(Guid chatGuid, Message message);
        void DeleteMessage(Guid chatGuid, Message message);
        Message? GetMessage(Guid chatGuid, Guid messageGuid);
        List<Message> GetMessages(Guid chatGuid, int skip, int take);
        int GetMessageCount(Guid chatGuid);
        void DeleteAllMessages(Guid chatGuid);
        string? GetLastMessagePreview(Guid chatGuid);
        IEnumerable<Guid> GetAllChatGuids();
    }
}
