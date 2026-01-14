using SharperLLM.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ShimmerChatLib
{
	public class Message
    {
        [JsonProperty]
        private List<ChatMessage> _versions = new List<ChatMessage>();
        
        [JsonProperty]
        private int _currentVersionIndex = 0;
        
        // 为了兼容旧版本，保留原始message属性的访问方式
        public ChatMessage message 
        {
            get => CurrentVersion;
            set 
            {
                // 如果是首次设置，添加到版本列表
                if (_versions.Count == 0)
                {
                    _versions.Add(value);
                    _currentVersionIndex = 0;
                }
                else
                {
                    // 替换当前版本
                    string oldContent = CurrentVersion?.Content ?? string.Empty;
                    _versions[_currentVersionIndex] = value;
                    
                    // 如果内容发生变化，触发事件
                    if (oldContent != value?.Content && ContentChanged != null)
                    {
                        ContentChanged(this, EventArgs.Empty);
                    }
                }
            }
        }
        
        // 版本管理相关属性
        public IReadOnlyList<ChatMessage> Versions => _versions.AsReadOnly();
        
        public int CurrentVersionIndex 
        { 
            get => _currentVersionIndex;
            set 
            {
                if (_versions != null && _versions.Count > 0 && value >= 0 && value < _versions.Count)
                {
                    _currentVersionIndex = value;
                    if (VersionChanged != null)
                    {
                        VersionChanged(this, EventArgs.Empty);
                    }
                }
            }
        }
        
        public ChatMessage CurrentVersion 
        { 
            get 
            { 
                if (_versions.Count == 0) return null;
                return _versions[_currentVersionIndex]; 
            }
        }
        
        public required DateTime timestamp { get; set; } // The timestamp of the message
        public required string sender { get; set; } // The sender of the message
        
        // 流式状态属性
        private bool _isStreaming;
        public bool IsStreaming 
        {
            get => _isStreaming;
            set 
            {
                _isStreaming = value;
                if (StreamingStateChanged != null)
                {
                    StreamingStateChanged(this, EventArgs.Empty);
                }
            }
        }
        
        // 添加版本管理方法
        public void AddVersion(ChatMessage newVersion)
        {
            _versions.Add(newVersion);
            _currentVersionIndex = _versions.Count - 1; // 切换到新版本
            
            if (VersionChanged != null)
            {
                VersionChanged(this, EventArgs.Empty);
            }
        }
        
        public void SwitchToVersion(int index)
        {
            if (index >= 0 && index < _versions.Count)
            {
                _currentVersionIndex = index;
                
                if (VersionChanged != null)
                {
                    VersionChanged(this, EventArgs.Empty);
                }
            }
        }
        
        public void RemoveVersion(int index)
        {
            if (_versions.Count <= 1) return; // 至少保留一个版本
            
            if (index >= 0 && index < _versions.Count)
            {
                _versions.RemoveAt(index);
                
                // 如果删除的是当前版本或之前的版本，调整索引
                if (index <= _currentVersionIndex)
                {
                    _currentVersionIndex = Math.Max(0, _currentVersionIndex - 1);
                }
                
                if (VersionChanged != null)
                {
                    VersionChanged(this, EventArgs.Empty);
                }
            }
        }
        
        public bool HasMultipleVersions => _versions.Count > 1;
        
        // 内容变化事件
        public event EventHandler ContentChanged;
        
        // 流式状态变化事件
        public event EventHandler StreamingStateChanged;
        
        // 版本变化事件
        public event EventHandler VersionChanged;
        
        // 反序列化兼容性处理 - 构造函数
        [JsonConstructor]
        public Message()
        {
            // 确保在反序列化时初始化列表
            if (_versions == null)
            {
                _versions = new List<ChatMessage>();
            }
            
            // 如果版本列表为空，说明可能是从旧格式反序列化的，需要特殊处理
            if (_versions.Count == 0)
            {
                // 对于旧格式，我们会将message属性的内容作为初始版本
                // 这个逻辑会在对象完全反序列化后处理
            }
        }
	}
}
