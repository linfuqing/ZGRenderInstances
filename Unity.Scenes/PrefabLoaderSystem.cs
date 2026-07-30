using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Scenes;

namespace ZG
{
    internal struct PrefabLoaderSingleton : IComponentData
    {
        public enum Status
        {
            None,
            Loaded,
            InProgressLoad
        }

        public struct Result
        {
            public Status status;
            public EntityPrefabReference entityPrefabReference;
        }

        public NativeQueue<Result> results;
    }

    public struct PrefabLoaderReferences : IComponentData
    {
        public NativeList<EntityPrefabReference> unloaded;
        public NativeList<EntityPrefabReference> loaded;
        public NativeList<EntityPrefabReference> inProgress;
        public uint releaseVersion;
        public bool isReleaseComplete;

        public PrefabLoaderReferences(AllocatorManager.AllocatorHandle allocator)
        {
            unloaded = new NativeList<EntityPrefabReference>(allocator);
            loaded = new NativeList<EntityPrefabReference>(allocator);
            inProgress = new NativeList<EntityPrefabReference>(allocator);
            releaseVersion = 0;
            isReleaseComplete = true;
        }

        public void Dispose()
        {
            unloaded.Dispose();
            loaded.Dispose();
            inProgress.Dispose();
        }
    }

    public struct PrefabLoader
    {
        public struct Writer
        {
            [ReadOnly]
            private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;

            private NativeQueue<PrefabLoaderSingleton.Result> __results;

            internal Writer(ref PrefabLoader value)
            {
                __weakAssetReferenceLoadingData = value.__weakAssetReferenceLoadingData;
                __results = value.__group.GetSingleton<PrefabLoaderSingleton>().results;
            }

            public bool TryGetOrLoadPrefabRoot(in EntityPrefabReference entityPrefabReference, out Entity entity)
            {
                if (PrefabLoaderSettings.isPaused)
                {
                    entity = Entity.Null;
                    return false;
                }

                PrefabLoaderSingleton.Result result;
                result.entityPrefabReference = entityPrefabReference;
                if (__weakAssetReferenceLoadingData.LoadedPrefabs.TryGetValue(entityPrefabReference,
                        out var loadedPrefab))
                {
                    entity = loadedPrefab.PrefabRoot;

                    result.status = PrefabLoaderSingleton.Status.Loaded;
                }
                else
                {
                    entity = Entity.Null;

                    result.status = __weakAssetReferenceLoadingData.InProgressLoads.ContainsKey(entityPrefabReference)
                        ? PrefabLoaderSingleton.Status.InProgressLoad
                        : PrefabLoaderSingleton.Status.None;
                }

                __results.Enqueue(result);

                return entity != Entity.Null;
            }
        }

        public struct ParallelWriter
        {
            [ReadOnly]
            private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;

            private NativeQueue<PrefabLoaderSingleton.Result>.ParallelWriter __results;

            internal ParallelWriter(ref PrefabLoader value)
            {
                __weakAssetReferenceLoadingData = value.__weakAssetReferenceLoadingData;
                __results = value.__group.GetSingleton<PrefabLoaderSingleton>().results.AsParallelWriter();
            }

            public bool TryGetOrLoadPrefabRoot(in EntityPrefabReference entityPrefabReference, out Entity entity)
            {
                UnityEngine.Assertions.Assert.IsTrue(entityPrefabReference.Id.IsValid);

                if (PrefabLoaderSettings.isPaused)
                {
                    entity = Entity.Null;
                    return false;
                }

                PrefabLoaderSingleton.Result result;
                result.entityPrefabReference = entityPrefabReference;
                if (__weakAssetReferenceLoadingData.LoadedPrefabs.TryGetValue(entityPrefabReference,
                        out var loadedPrefab))
                {
                    entity = loadedPrefab.PrefabRoot;

                    result.status = PrefabLoaderSingleton.Status.Loaded;
                }
                else
                {
                    entity = Entity.Null;

                    result.status = __weakAssetReferenceLoadingData.InProgressLoads.ContainsKey(entityPrefabReference)
                        ? PrefabLoaderSingleton.Status.InProgressLoad
                        : PrefabLoaderSingleton.Status.None;
                }

                __results.Enqueue(result);

                return entity != Entity.Null;
            }
        }

        [ReadOnly]
        private WeakAssetReferenceLoadingData __weakAssetReferenceLoadingData;

        private EntityQuery __group;

        public PrefabLoader(ref SystemState systemState)
        {
            var world = systemState.WorldUnmanaged;
            var entityManager = world.EntityManager;
            var systemHandle = world.GetExistingUnmanagedSystem<WeakAssetReferenceLoadingSystem>();
            __weakAssetReferenceLoadingData =
                entityManager.GetComponentData<WeakAssetReferenceLoadingData>(systemHandle);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAllRW<PrefabLoaderSingleton>()
                    .Build(ref systemState);
        }

        public PrefabLoader(SystemBase system)
        {
            var world = system.World;
            var entityManager = world.EntityManager;
            var systemHandle = world.GetExistingSystem<WeakAssetReferenceLoadingSystem>();
            __weakAssetReferenceLoadingData =
                entityManager.GetComponentData<WeakAssetReferenceLoadingData>(systemHandle);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __group = builder
                    .WithAllRW<PrefabLoaderSingleton>()
                    .Build(system);
        }

        /*public void AddDependency(in JobHandle jobHandle)
        {
            __group.AddDependency(jobHandle);
        }*/

        public Writer AsWriter()
        {
            return new Writer(ref this);
        }

        public ParallelWriter AsParallelWriter()
        {
            return new ParallelWriter(ref this);
        }
    }

    public static class PrefabLoaderSettings
    {
        public const float SAVED_TIME_MIN = 10.0f;

        internal enum Flag
        {
            Paused = 0x01,
            ReleaseAllRightNow = 0x02
        }

        public static readonly SharedStatic<int> Value = SharedStatic<int>.GetOrCreate<Flag>();

        private class SavedTime
        {
            public static readonly SharedStatic<float> Value = SharedStatic<float>.GetOrCreate<SavedTime>();
        }

        public static bool isPaused =>
            (Value.Data & (int)Flag.Paused) != 0;

        private static void _SetFlags(Flag flags)
        {
            int comparand;
            int result;
            do
            {
                comparand = Value.Data;
                result = comparand | (int)flags;
                if (result == comparand)
                    return;
            }
            while (System.Threading.Interlocked.CompareExchange(
                       ref Value.Data,
                       result,
                       comparand) != comparand);
        }

        public static float savedTime
        {
            get => math.max(SavedTime.Value.Data, SAVED_TIME_MIN);

            set => SavedTime.Value.Data = value;
        }

        public static void PauseLoading()
        {
            _SetFlags(Flag.Paused);
        }

        public static void ResumeLoading()
        {
            // A new activation supersedes every command from the old generation.
            System.Threading.Interlocked.Exchange(ref Value.Data, 0);
        }

        public static void ReleaseAllRightNow()
        {
            // Release is a one-shot command; Paused remains set after consumption.
            _SetFlags(Flag.Paused | Flag.ReleaseAllRightNow);
        }

        internal static bool _ConsumeReleaseAllRightNow()
        {
            int comparand;
            int result;
            do
            {
                comparand = Value.Data;
                if ((comparand & (int)Flag.ReleaseAllRightNow) == 0)
                    return false;

                result = comparand & ~(int)Flag.ReleaseAllRightNow;
            }
            while (System.Threading.Interlocked.CompareExchange(
                       ref Value.Data,
                       result,
                       comparand) != comparand);

            return true;
        }
    }

    [BurstCompile,
     CreateAfter(typeof(WeakAssetReferenceLoadingSystem)),
     UpdateInGroup(typeof(SceneSystemGroup)),
     UpdateBefore(typeof(WeakAssetReferenceLoadingSystem))]
    public partial struct PrefabLoaderSystem : ISystem
    {
        private struct Instance
        {
            public double time;
            public Entity entity;
        }

        private struct PendingUnload
        {
            public uint updateVersion;
            public EntityPrefabReference reference;
        }

        private struct Temp
        {
            public EntityPrefabReference entityPrefabReference;
            public Entity entity;
        }

        [BurstCompile]
        private struct Collect : IJob
        {
            public bool isPaused;
            public bool isReleasing;
            public float savedTime;
            public double time;
            public uint updateVersion;
            public int releaseCapacity;
            public PrefabLoaderReferences references;
            [ReadOnly]
            public NativeParallelHashSet<Hash128> retainedSceneGuids;
            public NativeList<PendingUnload> pendingUnloads;
            public NativeQueue<PrefabLoaderSingleton.Result> results;
            public NativeParallelHashMap<EntityPrefabReference, Instance> instances;
            public NativeList<Entity> entities;

            private void CancelPendingUnload(in EntityPrefabReference reference)
            {
                for (int i = pendingUnloads.Length - 1; i >= 0; --i)
                {
                    if (pendingUnloads[i].reference == reference)
                        pendingUnloads.RemoveAtSwapBack(i);
                }
            }

            private void QueuePendingUnload(in EntityPrefabReference reference)
            {
                for (int i = 0; i < pendingUnloads.Length; ++i)
                {
                    var pendingUnload = pendingUnloads[i];
                    if (pendingUnload.reference != reference)
                        continue;

                    pendingUnload.updateVersion = updateVersion;
                    pendingUnloads[i] = pendingUnload;
                    return;
                }

                pendingUnloads.Add(new PendingUnload
                {
                    updateVersion = updateVersion,
                    reference = reference
                });
            }

            private void FlushPendingUnloads()
            {
                for (int i = pendingUnloads.Length - 1; i >= 0; --i)
                {
                    var pendingUnload = pendingUnloads[i];
                    var reference = pendingUnload.reference;
                    if (pendingUnload.updateVersion == updateVersion ||
                        retainedSceneGuids.Contains(reference.AssetGUID) ||
                        instances.ContainsKey(reference) ||
                        references.loaded.IndexOf(reference) != -1 ||
                        references.inProgress.IndexOf(reference) != -1)
                    {
                        continue;
                    }

                    references.unloaded.Add(reference);

                    pendingUnloads.RemoveAtSwapBack(i);
                }
            }

            public void Execute()
            {
                references.loaded.Clear();

                Instance instance;
                Temp temp;
                NativeList<Temp> temps = default;
                if (isPaused)
                {
                    references.inProgress.Clear();
                    results.Clear();
                }
                else
                {
                    foreach (var reference in references.inProgress)
                    {
                        CancelPendingUnload(reference);

                        if (references.loaded.IndexOf(reference) == -1)
                            references.loaded.Add(reference);
                    }

                    references.inProgress.Clear();

                    while (results.TryDequeue(out var result))
                    {
                        if (instances.TryGetValue(result.entityPrefabReference, out instance))
                        {
                            CancelPendingUnload(result.entityPrefabReference);

                            temp.entityPrefabReference = result.entityPrefabReference;
                            temp.entity = instance.entity;

                            if (!temps.IsCreated)
                                temps = new NativeList<Temp>(results.Count, Allocator.Temp);

                            temps.Add(temp);

                            instances.Remove(result.entityPrefabReference);
                        }
                        else if (result.status == PrefabLoaderSingleton.Status.None &&
                                 references.inProgress.IndexOf(result.entityPrefabReference) == -1)
                        {
                            CancelPendingUnload(result.entityPrefabReference);

                            references.inProgress.Add(result.entityPrefabReference);
                        }
                    }
                }

                references.unloaded.Clear();
                NativeList<EntityPrefabReference> releasedReferences = default;
                if (isReleasing)
                {
                    foreach (var pair in instances)
                    {
                        instance = pair.Value;

                        QueuePendingUnload(pair.Key);

                        if (!releasedReferences.IsCreated)
                            releasedReferences = new NativeList<EntityPrefabReference>(
                                releaseCapacity,
                                Allocator.Temp);

                        releasedReferences.Add(pair.Key);

                        entities.Add(instance.entity);
                    }
                }
                else if (!isPaused)
                {
                    foreach (var pair in instances)
                    {
                        instance = pair.Value;
                        if (instance.time < time)
                        {
                            QueuePendingUnload(pair.Key);

                            if (!releasedReferences.IsCreated)
                                releasedReferences = new NativeList<EntityPrefabReference>(
                                    releaseCapacity,
                                    Allocator.Temp);

                            releasedReferences.Add(pair.Key);

                            entities.Add(instance.entity);

                            //continue;
                        }

                        //break;
                    }
                }

                if (releasedReferences.IsCreated)
                {
                    foreach (var reference in releasedReferences)
                        instances.Remove(reference);

                    releasedReferences.Dispose();
                }

                instance.time = time + savedTime;
                int numTemps = temps.IsCreated ? temps.Length : 0;
                if (numTemps > 0)
                {
                    for (int i = 0; i < numTemps; ++i)
                    {
                        temp = temps[i];

                        instance.entity = temp.entity;

                        instances.Add(temp.entityPrefabReference, instance);
                    }

                    temps.Dispose();
                }

                FlushPendingUnloads();
            }
        }

        [BurstCompile]
        private struct Apply : IJobParallelFor
        {
            public double time;

            [ReadOnly] 
            public NativeArray<Entity> entityArray;

            [ReadOnly] 
            public NativeArray<EntityPrefabReference> entityPrefabReferences;

            [NativeDisableParallelForRestriction]
            public ComponentLookup<RequestEntityPrefabLoaded> requestEntityPrefabLoadeds;

            public NativeParallelHashMap<EntityPrefabReference, Instance>.ParallelWriter instances;

            public void Execute(int index)
            {
                Instance instance;
                instance.time = time;
                instance.entity = entityArray[index];

                RequestEntityPrefabLoaded requestEntityPrefabLoaded;
                requestEntityPrefabLoaded.Prefab = entityPrefabReferences[index];

                instances.TryAdd(requestEntityPrefabLoaded.Prefab, instance);

                requestEntityPrefabLoadeds[instance.entity] = requestEntityPrefabLoaded;
            }
        }

        private ComponentLookup<RequestEntityPrefabLoaded> __requestEntityPrefabLoadeds;

        private EntityQuery __prefabLoadRequestQuery;
        private EntityQuery __sceneQuery;
        private EntityArchetype __entityArchetype;
        private NativeParallelHashMap<EntityPrefabReference, Instance> __instances;
        private NativeList<PendingUnload> __pendingUnloads;
        private NativeParallelHashSet<Hash128> __retainedSceneGuids;
        private NativeQueue<PrefabLoaderSingleton.Result> __results;
        private uint __updateVersion;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            __requestEntityPrefabLoadeds = state.GetComponentLookup<RequestEntityPrefabLoaded>();

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                builder
                    .WithAllRW<WeakAssetReferenceLoadingData>()
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                builder
                    .WithAllRW<PrefabLoaderSingleton>()
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                builder
                    .WithAllRW<PrefabLoaderReferences>()
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __sceneQuery = builder
                    .WithAll<SceneReference>()
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            using (var builder = new EntityQueryBuilder(Allocator.Temp))
                __prefabLoadRequestQuery = builder
                    .WithAll<RequestEntityPrefabLoaded>()
                    // CompleteLoad adds this component to the prefab root. The
                    // default Prefab exclusion keeps only real request entities.
                    .WithOptions(EntityQueryOptions.IncludeDisabledEntities)
                    .Build(ref state);

            var entityManager = state.EntityManager;
            using (var componentTypes = new NativeList<ComponentType>(Allocator.Temp)
                   {
                       ComponentType.ReadWrite<RequestEntityPrefabLoaded>()
                   })
                __entityArchetype = entityManager.CreateArchetype(componentTypes.AsArray());

            __instances = new NativeParallelHashMap<EntityPrefabReference, Instance>(1, Allocator.Persistent);
            __pendingUnloads = new NativeList<PendingUnload>(1, Allocator.Persistent);
            __retainedSceneGuids = new NativeParallelHashSet<Hash128>(1, Allocator.Persistent);
            __results = new NativeQueue<PrefabLoaderSingleton.Result>(Allocator.Persistent);

            PrefabLoaderSingleton singleton;
            singleton.results = __results;
            entityManager.CreateSingleton(singleton);

            var references = new PrefabLoaderReferences(Allocator.Persistent);
            entityManager.CreateSingleton(references);
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            __instances.Dispose();
            __pendingUnloads.Dispose();
            __retainedSceneGuids.Dispose();
            __results.Dispose();

            SystemAPI.GetSingleton<PrefabLoaderReferences>().Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var referencesRW = SystemAPI.GetSingletonRW<PrefabLoaderReferences>();
            var references = referencesRW.ValueRW;
            references.unloaded.Clear();

            state.CompleteDependency();

            bool isReleaseAllRightNow =
                PrefabLoaderSettings._ConsumeReleaseAllRightNow();
            bool isPaused = PrefabLoaderSettings.isPaused;
            int count = __results.Count;
            int instanceCount = __instances.Count();
            if (!isReleaseAllRightNow &&
                count < 1 &&
                references.inProgress.IsEmpty &&
                __pendingUnloads.IsEmpty)
                return;

            var worldUpdateAllocator = state.WorldUpdateAllocator;
            int releaseCapacity =
                isReleaseAllRightNow ? math.max(1, instanceCount) : 1;
            //var entityPrefabReferences = new NativeList<EntityPrefabReference>(count, worldUpdateAllocator);
            var entities = new NativeList<Entity>(
                isReleaseAllRightNow ? instanceCount : isPaused ? 0 : count,
                worldUpdateAllocator);

            float savedTime = PrefabLoaderSettings.savedTime;
            double time = SystemAPI.Time.ElapsedTime;

            ++__updateVersion;
            if (!__pendingUnloads.IsEmpty)
            {
                __retainedSceneGuids.Clear();

                int retainedGuidCapacity =
                    __sceneQuery.CalculateEntityCount() +
                    __prefabLoadRequestQuery.CalculateEntityCount();
                __retainedSceneGuids.Capacity = math.max(
                    __retainedSceneGuids.Capacity,
                    math.max(1, retainedGuidCapacity));

                using (var sceneReferences =
                       __sceneQuery.ToComponentDataArray<SceneReference>(Allocator.Temp))
                {
                    foreach (var sceneReference in sceneReferences)
                        __retainedSceneGuids.Add(sceneReference.SceneGUID);
                }

                using (var prefabLoadRequests =
                       __prefabLoadRequestQuery
                           .ToComponentDataArray<RequestEntityPrefabLoaded>(Allocator.Temp))
                {
                    foreach (var prefabLoadRequest in prefabLoadRequests)
                        __retainedSceneGuids.Add(prefabLoadRequest.Prefab.AssetGUID);
                }
            }

            Collect collect;
            collect.isPaused = isPaused;
            collect.isReleasing = isReleaseAllRightNow;
            collect.savedTime = savedTime;
            collect.time = time;
            collect.updateVersion = __updateVersion;
            collect.releaseCapacity = releaseCapacity;
            collect.references = references;
            collect.retainedSceneGuids = __retainedSceneGuids;
            collect.pendingUnloads = __pendingUnloads;
            collect.results = __results;
            collect.instances = __instances;
            //.entityPrefabReferences = entityPrefabReferences;
            collect.entities = entities;
            collect.RunByRef();

            if (isReleaseAllRightNow)
                unchecked
                {
                    ++references.releaseVersion;
                }

            references.isReleaseComplete = __pendingUnloads.IsEmpty;
            referencesRW.ValueRW = references;

            var entityManager = state.EntityManager;
            if (isReleaseAllRightNow &&
                !__prefabLoadRequestQuery.IsEmptyIgnoreFilter)
            {
                // Instantiated level entities can inherit RequestEntityPrefabLoaded
                // from a loaded prefab root. Those extra requesters keep this
                // loader's released GUIDs alive until the Unity scene is destroyed,
                // while the scene transition is waiting for this release to finish.
                // Break that cycle without touching requests for unrelated GUIDs.
                using var requestEntities =
                    __prefabLoadRequestQuery.ToEntityArray(Allocator.Temp);
                using var requests =
                    __prefabLoadRequestQuery
                        .ToComponentDataArray<RequestEntityPrefabLoaded>(
                            Allocator.Temp);
                using var releasedRequestEntities =
                    new NativeList<Entity>(requestEntities.Length, Allocator.Temp);
                for (int requestIndex = 0;
                     requestIndex < requests.Length;
                     ++requestIndex)
                {
                    var reference = requests[requestIndex].Prefab;
                    for (int pendingIndex = 0;
                         pendingIndex < __pendingUnloads.Length;
                         ++pendingIndex)
                    {
                        if (__pendingUnloads[pendingIndex].reference != reference)
                            continue;

                        releasedRequestEntities.Add(requestEntities[requestIndex]);
                        break;
                    }
                }

                if (!releasedRequestEntities.IsEmpty)
                {
                    var releasedRequestEntityArray =
                        releasedRequestEntities.AsArray();
                    entityManager.RemoveComponent<RequestEntityPrefabLoaded>(
                        releasedRequestEntityArray);
                    entityManager.RemoveComponent<PrefabLoadResult>(
                        releasedRequestEntityArray);
                }
            }

            if (!entities.IsEmpty)
                entityManager.DestroyEntity(entities.AsArray());

            if (!references.loaded.IsEmpty)
            {
                var entityArray =
                    entityManager.CreateEntity(__entityArchetype, references.loaded.Length, worldUpdateAllocator);

                __requestEntityPrefabLoadeds.Update(ref state);

                __instances.Capacity = math.max(__instances.Capacity, __instances.Count() + entityArray.Length);

                Apply apply;
                apply.time = time + savedTime;
                apply.entityArray = entityArray;
                apply.entityPrefabReferences = references.loaded.AsArray();
                apply.requestEntityPrefabLoadeds = __requestEntityPrefabLoadeds;
                apply.instances = __instances.AsParallelWriter();

                state.Dependency = apply.ScheduleByRef(entityArray.Length, 1, state.Dependency);
            }
        }
    }
}
