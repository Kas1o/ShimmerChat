# THEME

API Select Node 编辑器组件 硬编码了提示的颜色

AgentSelectPage Import 很丑

Embedding API 面板 需要适配

Qdrant API 面板 需要适配

Tokenizer 配置 面板 需要适配

节点编辑器那几个 复制粘贴 添加删除 按钮太丑了

新建 Agent 太丑了

Alternative Greetings 编辑体验需要优化

Agent 默认头像需要


编辑主题时出错
问题发生在 编辑颜色时。（无论用取色器还是修改 HEX 都可以复现）
```
warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100]
      Unhandled exception rendering component: Object of type 'Microsoft.AspNetCore.Components.ChangeEventArgs' cannot be converted to type 'System.String'.
      System.ArgumentException: Object of type 'Microsoft.AspNetCore.Components.ChangeEventArgs' cannot be converted to type 'System.String'.
         at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
         at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
         at System.Delegate.DynamicInvokeImpl(Object[] args)
         at Microsoft.AspNetCore.Components.EventCallbackWorkItem.InvokeAsync[T](MulticastDelegate delegate, T arg)
         at Microsoft.AspNetCore.Components.ComponentBase.Microsoft.AspNetCore.Components.IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, Object arg)
         at Microsoft.AspNetCore.Components.RenderTree.Renderer.DispatchEventAsync(UInt64 eventHandlerId, EventFieldInfo fieldInfo, EventArgs eventArgs, Boolean waitForQuiescence)
fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111]
      Unhandled exception in circuit '06BJWj3XxruEJuIFE1R_2mUmS7IALl1bK3MOkWXiZH0'.
      System.ArgumentException: Object of type 'Microsoft.AspNetCore.Components.ChangeEventArgs' cannot be converted to type 'System.String'.
         at System.Reflection.MethodBaseInvoker.InvokeWithOneArg(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
         at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
         at System.Delegate.DynamicInvokeImpl(Object[] args)
         at Microsoft.AspNetCore.Components.EventCallbackWorkItem.InvokeAsync[T](MulticastDelegate delegate, T arg)
         at Microsoft.AspNetCore.Components.ComponentBase.Microsoft.AspNetCore.Components.IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, Object arg)
         at Microsoft.AspNetCore.Components.RenderTree.Renderer.DispatchEventAsync(UInt64 eventHandlerId, EventFieldInfo fieldInfo, EventArgs eventArgs, Boolean waitForQuiescence)
```