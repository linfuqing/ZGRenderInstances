# 简述  
本项目基于DOTS，用于平替Unity的Entities Graphics包。  
## 优势  
* 1.可以在GLES 3.0&WebGL2.0环境运行。  
* 2.对比BatchRendererGroup有更好的渲染性能。
* 3.除了可以纯DOTS渲染，还支持与GameObject混用，Entity与GameObject一对一。逻辑使用ECS加速，而复杂动画、声音等使用GameObject，并使用Message机制传递消息。两种模式无缝替换。  
* 4.纯净模式支持MeshRenderer、SpriteRenderer。  
* 5.纯净模式支持SkinMeshRenderer并自动化生成GPU Skin Animation。  
## 原理  
* 1.使用CommandBuffer.DrawMeshInstanced+ConstantBuffer平替BatchRendererGroup+ComputeShader。  
* 2.不需要多次调用托管API，同时也不需要托管（比如BRG裁剪）回调，所有渲染数据在Job System一次整合好，能更好地利用CPU并行性能。  
* 3.最终整合成CommandBuffer，有更高的灵活度。  
* 4.Sprite数据传入ConstantBuffer，与MeshRenderer走渲染队列并在DOTS进行透明排序。  
* 5.根据SkinMeshRenderer自动生成GPU Animation Texture，在Vertex Shader读取并蒙皮。  
# 如何使用
在Unity中Window > Package Manager > + > Add package from git URL > 输入https://github.com/linfuqing/ZGRenderInstances.git  
如果不熟悉DOTS的使用，请先参考[Entites](https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/index.html)  
基本用法与[Entites Graphics](https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.3/manual/index.html)一致

