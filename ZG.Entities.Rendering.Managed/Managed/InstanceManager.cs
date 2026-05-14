#if ZG_ASSET_STREAMING
#define INSTANCE_ASSET_STREAMING
#endif

using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Events;

namespace ZG
{
    public sealed class InstanceManager : MonoBehaviour
    {
        private readonly struct InstanceID : IEquatable<InstanceID>
        {
#if INSTANCE_ASSET_STREAMING
            public readonly AssetBundleLoader<GameObject> Loader;
            
            public InstanceID(in Prefab prefab)
            {
                Loader = new AssetBundleLoader<GameObject>(
#if UNITY_EDITOR
                    UnityEditor.AssetDatabase.GetAssetPath(prefab.gameObject),
#else
                    prefab.gameObjectName, 
#endif
                    prefab.assetFilename, 
                    assetManager);
            }

            public bool Equals(InstanceID other)
            {
                return Loader.Equals(other.Loader);
            }

            public override int GetHashCode() => Loader.GetHashCode();
#else
            public readonly int Value;

            public InstanceID(in Prefab prefab)
            {
                Value = prefab.gameObject.GetInstanceID();
            }

            public bool Equals(InstanceID other)
            {
                return Value == other.Value;
            }

            public override int GetHashCode() => Value;
#endif
        }

        private class InstanceAsyncOperation
        {
            private AsyncInstantiateOperation<GameObject> __asyncInstantiateOperation;
            
#if INSTANCE_ASSET_STREAMING
            private Transform __parent;
            private AssetBundleLoader<GameObject> __loader;
            private int __instanceCount;
#endif
            
            public bool isDone
            {
                get
                {
                    if (gameObjects != null)
                        return true;
                    
                    if (__asyncInstantiateOperation == null)
                    {
#if INSTANCE_ASSET_STREAMING
                        if (!__loader.isDone)
                            return false;

                        return __Instantiate(__instanceCount, __loader.value, __parent);
#else
                        return true;
#endif
                    }
                    
                    if (__asyncInstantiateOperation.isDone)
                    {
                        gameObjects ??= __asyncInstantiateOperation.Result ?? Array.Empty<GameObject>();
                        
                        return true;
                    }

                    return false;
                }
            }

            public GameObject[] gameObjects
            {
                get;

                private set;
            }

            public InstanceAsyncOperation(int instanceCount, in InstanceID instanceID, Transform parent)
            {
                __asyncInstantiateOperation = null;
                
#if INSTANCE_ASSET_STREAMING
                __parent = parent;
                __loader = instanceID.Loader;
                __instanceCount = instanceCount;
#else
                __Instantiate(instanceCount, (GameObject)Resources.InstanceIDToObject(instanceID.Value), parent);
#endif
            }
            
            public void WaitForCompletion() => __asyncInstantiateOperation.WaitForCompletion();

            public void Cancel() => __asyncInstantiateOperation.Cancel();

            private bool __Instantiate(int instanceCount, GameObject prefab, Transform parent)
            {
                if (instanceCount > 0)
                {
                    if (instanceCount > 1)
                        __asyncInstantiateOperation = InstantiateAsync(prefab, instanceCount, parent);
                    else
                    {
                        gameObjects = new GameObject[instanceCount];
                        
                        for (int i = 0; i < instanceCount; ++i)
                            gameObjects[i] = Instantiate(prefab, parent);

                        return true;
                    }
                }

                return false;
            }
        }
        
        private struct Instance
        {
            public InstanceID instanceID;
            public float destroyTime;
            public string destroyMessageName;

            public UnityEngine.Object destroyMessageValue;
        }

        [Serializable]
        internal struct Prefab
        {
            public string name;
            public string destroyMessageName;
            
#if UNITY_EDITOR || INSTANCE_ASSET_STREAMING
            [HideInInspector] 
            public string gameObjectName;
            [HideInInspector] 
            public string assetFilename;
#endif
            
#if UNITY_EDITOR || !INSTANCE_ASSET_STREAMING
            public GameObject gameObject;
#endif
            
            public UnityEngine.Object destroyMessageValue;
            public float destroyTime;
            
#if UNITY_EDITOR && INSTANCE_ASSET_STREAMING
            public bool ToAssetBundleBuild(Dictionary<string, List<string>> assetNameMap)
            {
                string assetPath = UnityEditor.AssetDatabase.GetAssetPath(gameObject);
                bool isDirty = false, isContains = false;
                string assetBundleName;
                foreach (var pair in assetNameMap)
                {
                    if (pair.Value.Contains(assetPath))
                    {
                        assetBundleName = pair.Key;
                        if (assetBundleName != assetFilename)
                        {
                            assetFilename = assetBundleName;
                            
                            isDirty = true;
                        }

                        isContains = true;

                        break;
                    }
                }

                if (!isContains)
                {
                    if (!assetNameMap.TryGetValue(name, out var assetBundleNames))
                    {
                        assetBundleNames = new List<string>();

                        assetNameMap[name] = assetBundleNames;
                    }
                    
                    assetBundleNames.Add(assetPath);
                }

                if (assetPath != gameObjectName)
                {
                    gameObjectName = assetPath;

                    isDirty = true;
                }

                return isDirty;
            }
#endif
        }

        [Serializable]
        internal class StringEvent : UnityEvent<string>
        {

        }

        private sealed class Factory : MonoBehaviour
        {
            [Flags]
            private enum InstancesFlag
            {
                New = 0x01
            }

            private struct InstanceToDestroy
            {
                public double time;
                public InstanceID instanceID;
                public GameObject gameObject;
            }

            private struct InstancesToCreate
            {
                public InstancesFlag flag;
                public int entityCount;
                public Instance instance;
                public InstanceAsyncOperation asyncOperation;
                public InstanceManager manager;

                public int Submit(
                    int maxEntityCount,
                    int startIndex,
                    ref NativeList<Entity> entities,
                    List<GameObject> gameObjects,
                    SystemBase system)
                {
                    if ((flag & InstancesFlag.New) == InstancesFlag.New)
                    {
                        flag &= ~InstancesFlag.New;

                        return 0;
                    }

                    if (asyncOperation != null && asyncOperation.isDone)
                    {
                        bool isDone = asyncOperation.isDone;
                        if (!isDone && maxEntityCount == int.MaxValue)
                        {
                            isDone = true;

                            UnityEngine.Profiling.Profiler.BeginSample("WaitForCompletion");

                            asyncOperation.WaitForCompletion();

                            UnityEngine.Profiling.Profiler.EndSample();
                        }

                        if (isDone)
                        {
                            UnityEngine.Profiling.Profiler.BeginSample("AsyncInstantiateOperation Done");

                            var results = asyncOperation.gameObjects;
                            int numResults = results == null ? 0 : results.Length;
                            if (numResults > 0)
                            {
                                maxEntityCount = Mathf.Max(maxEntityCount, numResults);

                                GameObject result;
                                int entityIndex = startIndex + this.entityCount - 1;
                                for (int i = 0; i < numResults; ++i)
                                    gameObjects[entityIndex - i] = results[i];

                                if (manager != null)
                                {
                                    if (__instanceManagers == null)
                                        __instanceManagers = new Dictionary<int, InstanceManager>();

                                    if (manager.__instances == null)
                                        manager.__instances = new Dictionary<int, Instance>();

                                    int transformInstanceID;
                                    for (int i = 0; i < numResults; ++i)
                                    {
                                        result = results[i];
                                        if (result == null)
                                            continue;

                                        transformInstanceID = result.transform.GetInstanceID();

                                        __instanceManagers.Add(transformInstanceID, manager);

                                        manager.__instances.Add(transformInstanceID, instance);
                                    }
                                }
                            }

                            asyncOperation = null;

                            UnityEngine.Profiling.Profiler.EndSample();
                        }
                    }

                    UnityEngine.Profiling.Profiler.BeginSample("Check");

                    int index, entityCount = Mathf.Min(this.entityCount, maxEntityCount);
                    Entity entity;
                    var entityManager = system.EntityManager;
                    for (int i = 0; i < entityCount; ++i)
                    {
                        index = i + startIndex;
                        if (gameObjects[index] == null && asyncOperation != null)
                        {
                            entityCount = i;

                            break;
                        }

                        entity = entities[index];
                        if (entityManager.HasComponent<ZG.Instance>(entity))
                        {
                            if (entityManager.IsEnabled(entity))
                            {
                                if (gameObjects[index] != null)
                                    continue;
                            }
                            else
                                entityManager.SetComponentEnabled<ZG.Instance>(entity, true);
                        }
                        
                        __Destroy(gameObjects[index], instance.instanceID);

                        gameObjects.RemoveAt(index);

                        entities.RemoveAt(index);

                        --i;

                        --entityCount;

                        --this.entityCount;
                    }

                    UnityEngine.Profiling.Profiler.EndSample();

                    if (entityCount < 1)
                        return 0;

                    this.entityCount -= entityCount;

                    UnityEngine.Profiling.Profiler.BeginSample("AddComponent");

                    system.EntityManager.AddComponent<CopyMatrixToTransformInstanceID>(
                        entities.AsArray().GetSubArray(startIndex, entityCount));

                    UnityEngine.Profiling.Profiler.EndSample();

                    UnityEngine.Profiling.Profiler.BeginSample("GetComponentLookup");

                    var instanceIDs = system.GetComponentLookup<CopyMatrixToTransformInstanceID>();
                    var localToWorlds = system.GetComponentLookup<LocalToWorld>(true);

                    UnityEngine.Profiling.Profiler.EndSample();

                    UnityEngine.Profiling.Profiler.BeginSample("Apply");

                    CopyMatrixToTransformInstanceID instanceID;
                    instanceID.isSendMessageOnDestroy = false;

                    GameObject gameObject;
                    Transform transform;
                    LocalToWorld localToWorld;
                    for (int i = 0; i < entityCount; ++i)
                    {
                        index = i + startIndex;

                        gameObject = gameObjects[index];

                        transform = gameObject.transform;

                        entity = entities[index];

#if UNITY_EDITOR
                        entityManager.SetName(entity, $"{gameObject.name}({transform.GetInstanceID()})");
#endif

                        if (localToWorlds.TryGetComponent(entity, out localToWorld))
                        {
                            transform.localPosition = localToWorld.Position;
                            transform.localRotation = localToWorld.Rotation;
                        }

                        gameObject.SetActive(true);

                        instanceID.value = transform.GetInstanceID();

                        instanceIDs[entity] = instanceID;
                    }

                    UnityEngine.Profiling.Profiler.EndSample();

                    UnityEngine.Profiling.Profiler.BeginSample("RemoveRange");

                    gameObjects.RemoveRange(startIndex, entityCount);
                    entities.RemoveRange(startIndex, entityCount);

                    UnityEngine.Profiling.Profiler.EndSample();

                    return entityCount;
                }
            }

            private class System
            {
                private NativeList<Entity> __entities;
                private List<GameObject> __results;
                private List<InstancesToCreate> __instances;

                public System()
                {
                    __entities = new NativeList<Entity>(Allocator.Persistent);
                    __results = new List<GameObject>();
                    __instances = new List<InstancesToCreate>();
                }

                ~System()
                {
                    Dispose();
                }

                public void Dispose()
                {
                    if(__entities.IsCreated)
                        __entities.Dispose();

                    foreach (var instance in __instances)
                    {
                        if (instance.asyncOperation == null)
                            continue;

                        if (instance.asyncOperation.isDone)
                        {
                            foreach (var gameObject in instance.asyncOperation.gameObjects)
                                DestroyImmediate(gameObject);
                        }
                        else
                            instance.asyncOperation.Cancel();
                    }
                }

                public void Apply(
                    in Instance instance,
                    in NativeArray<Entity> entities,
                    IEnumerable<GameObject> results,
                    InstanceAsyncOperation asyncOperation,
                    InstanceManager manager)
                {
                    __entities.AddRange(entities);

                    int numResults = __results.Count;
                    if (results != null)
                        __results.AddRange(results);

                    InstancesToCreate result;
                    result.entityCount = entities.Length;
                    for (int i = __results.Count - numResults; i < result.entityCount; ++i)
                        __results.Add(null);

                    UnityEngine.Assertions.Assert.AreEqual(__results.Count, __entities.Length);

                    result.flag = InstancesFlag.New;
                    result.instance = instance;
                    result.asyncOperation = asyncOperation;
                    result.manager = manager;

                    __instances.Add(result);
                }

                public int Submit(
                    int maxEntityCount,
                    SystemBase system)
                {
                    UnityEngine.Profiling.Profiler.BeginSample("System Submit");

                    InstancesToCreate instance;
                    int count = 0, startIndex = 0, numInstances = __instances.Count;
                    for (int i = 0; i < numInstances; ++i)
                    {
                        instance = __instances[i];
                        count += instance.Submit(
                            maxEntityCount == int.MaxValue ? maxEntityCount : maxEntityCount - count,
                            startIndex,
                            ref __entities,
                            __results,
                            system);

                        startIndex += instance.entityCount;

                        if (instance.entityCount < 1)
                        {
                            __instances.RemoveAt(i--);

                            --numInstances;
                        }
                        else
                            __instances[i] = instance;

                        if (count == maxEntityCount)
                            break;
                    }

                    UnityEngine.Profiling.Profiler.EndSample();
                    return count;
                }
            }

            private List<InstanceToDestroy> __instancesToDestroy = new List<InstanceToDestroy>();

            private Dictionary<SystemBase, System> __systems = new Dictionary<SystemBase, System>();

            private static Factory __instance;

            public static Factory instance
            {
                get
                {
                    if (__instance == null)
                    {
                        var temp = new GameObject();
                        temp.hideFlags = HideFlags.HideAndDontSave;
                        DontDestroyOnLoad(temp);

                        __instance = temp.AddComponent<Factory>();
                    }

                    return __instance;
                }
            }

            public void WaitForCompletion()
            {
                foreach (var system in __systems)
                    system.Value.Submit(int.MaxValue, system.Key);

                int numInstancesToDestroy = __instancesToDestroy == null ? 0 : __instancesToDestroy.Count;
                if (numInstancesToDestroy > 0)
                {
                    double time = Time.timeAsDouble;
                    InstanceToDestroy instance;
                    for (int i = 0; i < numInstancesToDestroy; ++i)
                    {
                        instance = __instancesToDestroy[i];
                        if (instance.time > time)
                            continue;

                        __Destroy(instance.gameObject, instance.instanceID);

                        __instancesToDestroy.RemoveAtSwapBack(i--);

                        --numInstancesToDestroy;
                    }
                }
            }

            public void Create(
                in Instance instance,
                in NativeArray<Entity> entities,
                IEnumerable<GameObject> results,
                InstanceAsyncOperation asyncOperation,
                InstanceManager manager,
                SystemBase system)
            {
                if (!__systems.TryGetValue(system, out var value))
                {
                    value = new System();

                    __systems[system] = value;
                }

                value.Apply(instance, entities, results, asyncOperation, manager);
            }

            public void Destroy(in InstanceID instanceID, float time, GameObject gameObject)
            {
                if (time > Mathf.Epsilon)
                {
                    InstanceToDestroy instance;
                    instance.time = Time.timeAsDouble + time;
                    instance.instanceID = instanceID;
                    instance.gameObject = gameObject;

                    if (__instancesToDestroy == null)
                        __instancesToDestroy = new List<InstanceToDestroy>();

                    __instancesToDestroy.Add(instance);
                }
                else
                    __Destroy(gameObject, instanceID);
            }

            void OnDestroy()
            {
                foreach (var system in __systems.Values)
                    system.Dispose();
            }

            void Update()
            {
                float deltaTime = Time.maximumDeltaTime * 0.5f;
                long tick = DateTime.Now.Ticks;
                foreach (var system in __systems)
                {
                    while (system.Value.Submit(16, system.Key) > 0)
                    {
                        if ((DateTime.Now.Ticks - tick) * 1.0 / TimeSpan.TicksPerSecond > deltaTime)
                            return;
                    }
                }

                int numInstancesToDestroy = __instancesToDestroy == null ? 0 : __instancesToDestroy.Count;
                if (numInstancesToDestroy > 0)
                {
                    double time = Time.timeAsDouble;
                    InstanceToDestroy instance;
                    for (int i = 0; i < numInstancesToDestroy; ++i)
                    {
                        instance = __instancesToDestroy[i];
                        if (instance.time > time)
                            continue;

                        __Destroy(instance.gameObject, instance.instanceID);

                        __instancesToDestroy.RemoveAtSwapBack(i--);

                        --numInstancesToDestroy;
                    }
                }
            }
        }

        [SerializeField] [UnityEngine.Serialization.FormerlySerializedAs("_onAcitveCount")]
        internal StringEvent _onActiveCount;

        //public UnityEngine.Object TEMP;
        [SerializeField] 
        internal Prefab[] _prefabs;

        private static Dictionary<string, (InstanceManager, int)> __prefabIndices;

        private static Dictionary<InstanceID, List<GameObject>> __gameObjects;

        private static Dictionary<int, InstanceManager> __instanceManagers;

        private Dictionary<int, Instance> __instances;
        
#if INSTANCE_ASSET_STREAMING
        public static AssetManager assetManager;
#endif

        //private HashSet<AsyncInstantiateOperation<GameObject>> __results;

        public static int activeCount { get; private set; }

#if UNITY_EDITOR && INSTANCE_ASSET_STREAMING
        public static void ToAssetBundleBuild(Dictionary<string, List<string>> assetNameMap)
        {
            var assetPaths = new List<string>();
            foreach (var assetNames in assetNameMap.Values)
                assetPaths.AddRange(assetNames);
            
            //string[] guids = UnityEditor.AssetDatabase.FindAssets("t:prefab");
            string assetPath;
            GameObject gameObject;
            int i, j, numPrefabs, numAssetPaths = assetPaths.Count;
            bool result;
            for (i = 0; i < numAssetPaths; ++i)
            {
                if (UnityEditor.EditorUtility.DisplayCancelableProgressBar("Collect Instance Manager", i.ToString() + "/" + numAssetPaths, i * 1.0f / numAssetPaths))
                    break;
                
                assetPath = assetPaths[i];
                gameObject = UnityEditor.PrefabUtility.LoadPrefabContents(assetPath);

                result = false;
                foreach (var instanceManager in gameObject.GetComponentsInChildren<InstanceManager>(true))
                {
                    numPrefabs = instanceManager._prefabs == null ? 0 : instanceManager._prefabs.Length;
                    for (j = 0; j < numPrefabs; ++j)
                        result = instanceManager._prefabs[j].ToAssetBundleBuild(assetNameMap) || result;
                }

                if(result)
                    UnityEditor.PrefabUtility.SaveAsPrefabAsset(gameObject, assetPath);
                
                UnityEditor.PrefabUtility.UnloadPrefabContents(gameObject);
            }
            
            UnityEditor.EditorUtility.ClearProgressBar();
        }
#endif

        public static void Destroy(int instanceID, bool isSendMessage)
        {
            if (__instanceManagers == null ||
                !__instanceManagers.TryGetValue(instanceID, out var instanceManager) ||
                instanceManager.__instances == null ||
                !instanceManager.__instances.TryGetValue(instanceID, out var instance))
            {
                Debug.LogWarning($"Destroy {instanceID} has been failed!", Resources.InstanceIDToObject(instanceID));

                return;
            }

            instanceManager.__Destroy(instanceID, instance, isSendMessage);
            //instanceManager.StartCoroutine(instanceManager.__Destroy(instanceID, instance, isSendMessage));
        }

        public static void Instantiate(
            string name,
            SystemBase system,
            in NativeArray<Entity> entities //, 
            //in ComponentLookup<LocalToWorld> localToWorlds, 
            //ref ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs
        )
        {
            if (!__prefabIndices.TryGetValue(name, out var prefabIndex))
            {
                Debug.LogError($"The prefab {name} can not been found!");

                return;
            }

            var manager = prefabIndex.Item1;

            //manager.StartCoroutine(
            manager.__Instantiate(
                manager._prefabs[prefabIndex.Item2],
                system,
                entities //,
                //instanceIDs,
                //localToWorlds
            );
            //);
        }

        private void __Instantiate(
            Prefab prefab,
            SystemBase system,
            NativeArray<Entity> entities
            //ComponentLookup<CopyMatrixToTransformInstanceID> instanceIDs, 
            //ComponentLookup<LocalToWorld> localToWorlds
        )
        {
            int numGameObjects, numEntities = entities.Length;
            var instanceID = new InstanceID(prefab);
            List<GameObject> results = null;
            if (__gameObjects != null && __gameObjects.TryGetValue(instanceID, out var gameObjects))
            {
                numGameObjects = gameObjects == null ? 0 : gameObjects.Count;
                if (numGameObjects > 0)
                {
                    results = new List<GameObject>(numEntities);

                    if (numGameObjects > numEntities)
                    {
                        int temp = numGameObjects - numEntities;
                        for (int i = numGameObjects - 1; i >= temp; --i)
                            results.Add(gameObjects[i]);

                        gameObjects.RemoveRange(temp, numEntities);

                        numGameObjects = 0;
                    }
                    else
                    {
                        numGameObjects = numEntities - numGameObjects;

                        results.AddRange(gameObjects);

                        gameObjects.Clear();
                    }
                }
                else
                    numGameObjects = numEntities;
            }
            else
                numGameObjects = numEntities;

            Instance instance;
            instance.instanceID = instanceID;
            instance.destroyTime = prefab.destroyTime;
            instance.destroyMessageName = prefab.destroyMessageName;
            instance.destroyMessageValue = prefab.destroyMessageValue;

            /*AsyncInstantiateOperation<GameObject> asyncInstantiateOperation = null;
            if (numGameObjects > 0)
            {
                if (numGameObjects > 1)
                    asyncInstantiateOperation = InstantiateAsync(prefab.gameObject, numGameObjects, this.transform);
                else
                {
                    int transformInstanceID;
                    GameObject result;
                    for (int i = 0; i < numGameObjects; ++i)
                    {
                        result = Instantiate(prefab.gameObject, this.transform);

                        transformInstanceID = result.transform.GetInstanceID();

                        if (__instanceManagers == null)
                            __instanceManagers = new Dictionary<int, InstanceManager>();

                        __instanceManagers.Add(transformInstanceID, this);

                        if (__instances == null)
                            __instances = new Dictionary<int, Instance>();

                        __instances.Add(transformInstanceID, instance);

                        if (results == null)
                            results = new List<GameObject>();

                        results.Add(result);
                    }
                }
            }*/

            Factory.instance.Create(
                instance,
                entities,
                results,
                numGameObjects > 0 ? new InstanceAsyncOperation(numGameObjects, instanceID, transform) : null, 
                //asyncInstantiateOperation,
                this,
                system);
        }

        private void __Destroy(int instanceID, Instance instance, bool isSendMessage)
        {
            --activeCount;

            if (_onActiveCount != null)
                _onActiveCount.Invoke(activeCount.ToString());

            var transform = Resources.InstanceIDToObject(instanceID) as Transform;
            var gameObject = transform == null ? null : transform.gameObject;
            if (gameObject == null)
                return;

            if (isSendMessage)
            {
                if (!string.IsNullOrEmpty(instance.destroyMessageName))
                    gameObject.BroadcastMessage(instance.destroyMessageName, instance.destroyMessageValue);

                //yield return new WaitForSeconds(instance.destroyTime);
                Factory.instance.Destroy(instance.instanceID, instance.destroyTime, gameObject);
            }
            else
                __Destroy(gameObject, instance.instanceID);
        }

        private static void __Destroy(GameObject gameObject, in InstanceID instanceID)
        {
            if (gameObject == null)
                return;

            if (__gameObjects == null)
                __gameObjects = new Dictionary<InstanceID, List<GameObject>>();

            if (!__gameObjects.TryGetValue(instanceID, out var gameObjects))
            {
                gameObjects = new List<GameObject>();

                __gameObjects[instanceID] = gameObjects;
            }

            gameObject.SetActive(false);

            gameObjects.Add(gameObject);
        }

        void OnEnable()
        {
            if (__prefabIndices == null)
                __prefabIndices = new Dictionary<string, (InstanceManager, int)>(_prefabs.Length);

            int numPrefabs = _prefabs.Length;
            for (int i = 0; i < numPrefabs; ++i)
            {
#if UNITY_EDITOR
                UnityEngine.Assertions.Assert.IsNotNull(_prefabs[i].gameObject, $"{_prefabs[i].name} 不能为空！");
                UnityEngine.Assertions.Assert.IsFalse(_prefabs[i].gameObject.activeSelf, $"{_prefabs[i].name} 默认必须关闭！");
                UnityEngine.Assertions.Assert.IsNull(_prefabs[i].gameObject.GetComponentInChildren<Collider>(true), $"{_prefabs[i].name} 包含碰撞体!");
#endif
                __prefabIndices.Add(_prefabs[i].name, (this, i));
            }
        }

        void OnDisable()
        {
            if (__prefabIndices != null)
            {
                int numPrefabs = _prefabs.Length;
                for (int i = 0; i < numPrefabs; i++)
                    __prefabIndices.Remove(_prefabs[i].name);
            }

            Factory.instance.WaitForCompletion();

            /*if (__results != null)
            {
                foreach (var result in __results)
                    result.Cancel();

                __results.Clear();
            }*/

            if (__instances != null)
            {
                int i, numGameObjects, transformInstanceID;
                InstanceID instanceID;
                Transform transform;
                GameObject gameObject;
                List<GameObject> gameObjects;
                foreach (var pair in __instances)
                {
                    transformInstanceID = pair.Key;

                    __instanceManagers.Remove(transformInstanceID);

                    instanceID = pair.Value.instanceID;
                    if (__gameObjects != null && __gameObjects.TryGetValue(instanceID, out gameObjects))
                    {
                        numGameObjects = gameObjects.Count;
                        for (i = 0; i < numGameObjects; ++i)
                        {
                            gameObject = gameObjects[i];
                            if (gameObject == null || gameObject.transform.GetInstanceID() == transformInstanceID)
                            {
                                gameObjects.RemoveAt(i);

                                if (numGameObjects == 1)
                                    __gameObjects.Remove(instanceID);

                                break;
                            }
                        }
                    }

                    transform = Resources.InstanceIDToObject(transformInstanceID) as Transform;
                    if (transform != null)
                        Destroy(transform.gameObject);
                }

                __instances.Clear();
            }
        }
    }
}