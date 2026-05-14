#if ZG_ASSET_STREAMING
#define INSTANCE_ASSET_STREAMING
#endif

using System.Collections.Generic;

#if INSTANCE_ASSET_STREAMING
namespace ZG
{
    public struct InstanceAssetBundleBuildCollector : IAssetBundleBuildCollector
    {
        public void Collect(Dictionary<string, List<string>> assetNameMap)
        {
            InstanceManager.ToAssetBundleBuild(assetNameMap);
        }
    }
}
#endif