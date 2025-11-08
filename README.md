# 简述  
本项目基于DOTS，用于平替Unity的Entities Graphics包。  
## 优势  
* 1.可以在GLES 3.0&WebGL2.0环境运行。  
* 2.对比BatchRendererGroup有更好的CPU性能。  
* 3.除了可以纯ECS渲染，还支持与GameObject混用，Entity与GameObject一对一。逻辑使用ECS加速，而复杂动画、声音等使用GameObject，并使用Message机制传递消息。
* 4.支持直接Bake网格渲染器MeshRenderer及精灵渲染器SpriteRenderer。  
* 5.支持直接Bake蒙皮渲染器SkinMeshRenderer并自动化生成GPU Skin Animation。  
## 原理  
* 1.使用CommandBuffer.DrawMeshInstanced+ConstantBuffer平替BatchRendererGroup+ComputeShader。  
* 2.不需要多次调用托管API，同时也不需要托管（比如BRG裁剪）回调，所有渲染数据在Job System一次整合好，能更好地利用CPU并行性能。  
* 3.最终整合成CommandBuffer，有更高的灵活度。  
* 4.Sprite数据传入ConstantBuffer，与MeshRenderer走渲染队列并在ECS进行透明排序。  
* 5.根据SkinMeshRenderer自动生成GPU Animation Texture，在Vertex Shader读取并蒙皮。  
# 如何使用  
在Unity中Window>Package Manager>+>Add package from git URL>输入https://github.com/linfuqing/ZGRenderInstances.git  
如果不熟悉DOTS的使用，请先参考[Entites](https://docs.unity3d.com/Packages/com.unity.entities@1.3/manual/index.html)，基本用法与[Entites Graphics](https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.3/manual/index.html)一致。  
## Getting started  
* 1.创建一个空的Unity URP工程。
* 2.选中Universal Renderer Data（默认创建的三个名字分别为URP-Balanced-Renderer，URP-HighFidelity-Renderer，URP-Performant-Renderer）>Add Renderer Feature>Render Instances Pass Feature，并把Rendering Path改为Forward+。  
* 3.创建一个子场景：选中场景右键添加子场景：New Empty Sub Scene，直接保存为New Sub Scene。  
* 4.在子场景内创建一个Cube：New Sub Scene右键GameObject>3D Object>Cube。  
* 5.运行，你会看到Cube已经以ECS的方式渲染出来了。Cube的Entity可以通过Entities Hierarchy（Window>Entities>Hierarchy）进行查看。  
## Instance方案  
Instance是一个套Entity与GameObject一对一同步的解决方案。Instance的核心组件包括：  
* InstanceManager：管理GameObject的管理器，放置在子场景（SubScene）之外（不会被Bake成Entity）。  
* InstanceAuthoring：ECS组件，Bake后会自动在InstanceManager寻找同名的配置，并实列化对应的Prefab。并实时同步实例化后GameObject上的Transform（对应Entity的LocalTransform）及GameObject开关（对应Entity的开关Disabled）  
* MessageAuthoring：自定义消息，可以通过Entity端调用GameObject上Component对应的方法并传参以实现更多的自定义逻辑。  
### 案例  
比如要实现一个游戏NPC，我们把NPC的所有AI逻辑都放在ECS内实现，同时要求这个NPC在不同状态播放对应的不同音效。通过Instance就可以实现这个需求：   
	* 1.创建一个Prefab，并在这个Prefab上挂载一个AudioSource组件。把这个Prefab配置到场景里对应的InstanceManager上，取名为“NPC”。  
	* 2.在子场景里，该NPC要Bake成Entity的GameObject上挂载InstanceAuthoring和MessageAuthoring，并在InstanceAuthoring的NameOverride上填写对应InstanceManager上的Prefab配置名“NPC”。  
	* 3.在ECS里实现播放对应的AudioClip逻辑：
```c#
BufferLookup<Message> messageLookup;  
UnityObjectRef<UnityEngine.Object> audioClipToPlay; //要播放的AudioClip，需要在Bake的时候转化成UnityObjectRef<UnityEngine.Object>

...  

DynamicBuffer<Message> messages = messageLookup[npcEntity];  

Message message;
message.key = 0;
message.name = "PlayOneShot";   //填写要调用的AudioSource方法名PlayOneShot
message.value = audioClipToPlay; 
messages.Add(message);

messageLookup.SetCompoenntEnabled(entity, true);
```
## InstanceSkinnedMesh方案  
InstanceSkinnedMesh是一套可以把对应预制体的所有SkinMeshRenderer全部Bake成GPU Animation Texture Array并在ECS渲染的解决方案，附带基础的动画播放的能力。  
* SkinnedInstanceNode：要渲染GPU Animation，需要在Shader Graph加入此节点来生成Shader。  
* SkinnedMeshRendererDatabase：在Assets右键Create/ZG/Skinned Mesh Renderer Database创建，可以通过引用多个含SkinnedMeshRenderer的Prefab来生成GPU Animation Texture Array并根据材质模板（需要SkinnedInstanceNode的Shader）生成可渲染的Material并填充。  
* SkinnedMeshRendererAuthoring：可以通过引用的Prefab来自动查找并复用工程内创建好的GPU Animation Texture Array及Material，在ECS里直接渲染。  
* InstanceAnimationMessageAuthoring：可以使Message对动画进行控制。
有的时候你可能希望同时用SkinnedMeshRendererAuthoring和UnityEngine.Animator来渲染不同的怪物，比如大量的小怪使用SkinnedMeshRendererAuthoring来进行Instance优化，而BOSS使用Instance方案桥接GameObject端的Animator来保证动画混合及灵活度。
此时通过同一套Message来管理动画播放是逻辑和渲染分离的有效办法。    
## InstanceSprite方案  
InstanceSprite是一套可以把SpriteRenderer直接Bake成ECS组件自动复用Sprite并渲染的解决方案。为了最大化复用，所有Sprite需要先集合成[SpriteAtlas](https://docs.unity3d.com/2022.3/Documentation/Manual/sprite-atlas.html)才能使用。
* SpriteAltasDatabase：在Assets右键Create/ZG/Sprite Altas Database创建，用来管理SpriteAtlas，所有SpriteRenderer的Sprite都需要集合成SpriteAtlas并被SpriteAltasDatabase引用，才能在Bake时被正确识别。  
### 快速使用  
* 1.Edit>Project Setting..>Editor>Sprite Packer>Mode选成Sprite Atlas V2 - Enabled。  
* 2.在Assets右键Create/2D/Sprite Altas创建SpriteAtlas并引用需要渲染的Sprite。  
* 3.在Assets右键Create/ZG/Sprite Altas Database创建SpriteAltasDatabase并引用SpriteAtlas。  
* 4.把要渲染的SpriteRenderer拖入子场景进行Bake。
# FAQ  
## 为什么需要Forward+  
因为DrawMeshInstanced不支持per-instance data，所以URP的前向光照无效（也可能是BUG）。