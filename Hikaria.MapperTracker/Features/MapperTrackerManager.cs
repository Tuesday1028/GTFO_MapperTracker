using Gear;
using GTFO.API;
using Player;
using SNetwork;
using System;
using System.Collections.Generic;
using TheArchive.Core.Attributes;
using TheArchive.Core.Attributes.Feature.Settings;
using TheArchive.Core.FeaturesAPI;
using TheArchive.Core.Models;
using TheArchive.Loader;
using TheArchive.Utilities;
using UnityEngine;

namespace Hikaria.MapperTracker.Handlers
{
    [DisallowInGameToggle]
    [DoNotSaveToConfig]
    [EnableFeatureByDefault]
    public class MapperTrackerManager : Feature
    {
        public override string Name => "地形映射生物追踪器";

        public override string Group => FeatureGroups.GetOrCreate("地形映射生物追踪器");

        [FeatureConfig]
        public static MapperTrackerSettings Settings { get; set; }

        public class MapperTrackerSettings
        {
            [FSHeader("常规设置")]
            [FSDisplayName("启用")]
            [FSDescription("仅本地启用与禁用")]
            public bool EnableMapperTracker { get; set; } = true;

            [FSDisplayName("切换按键")]
            public KeyCode ToggleKey { get; set; } = KeyCode.X;
            [FSHeader("映射参数设置")]
            [FSDisplayName("默认扫描视野")]
            [FSDescription("同步")]
            public float FieldOfView { get; set; } = 65f;
            [FSDisplayName("聚焦扫描视野")]
            [FSDescription("同步")]
            public float FieldOfViewFocused { get; set; } = 35f;
            [FSDisplayName("默认扫描最大距离")]
            [FSDescription("同步")]
            public float MaxDistanceDefault { get; set; } = 20f;
            [FSDisplayName("聚焦扫描最大距离")]
            [FSDescription("同步")]
            public float MaxDistanceFocused { get; set; } = 40f;
            [FSDisplayName("默认X-Ray每秒更新数量")]
            public int RaysPerSecondDefault { get; set; } = 50000;
            [FSDisplayName("聚焦X-Ray每秒更新数量")]
            public int RaysPerSecondFocused { get; set; } = 10000;
            [FSHeader("映射颜色与大小设置")]
            [FSDisplayName("地形颜色")]
            public SColor DefaultColor { get; set; } = new(0, 1f, 0.9709f, 0.1608f);
            [FSDisplayName("地形大小")]
            public float DefaultSize { get; set; } = 0.5f;
            [FSDisplayName("敌人颜色")]
            public SColor EnemyColor { get; set; } = new(1f, 0f, 0f, 0.502f);
            [FSDisplayName("敌人大小")]
            public float EnemySize { get; set; } = 1f;
            [FSDisplayName("可交互物品颜色")]
            public SColor InteractionColor { get; set; } = new(1f, 0.4911f, 0f, 0.0784f);
            [FSDisplayName("可交互物品大小")]
            public float InteractionSize { get; set; } = 1f;
        }

        public override void Init()
        {
            LoaderWrapper.ClassInjector.RegisterTypeInIl2Cpp<MapperTrackerController>();
            NetworkAPI.RegisterEvent<pMapperTrackerXRayData>(typeof(pMapperTrackerXRayData).FullName, MapperTrackerController.ReceiveMapperXRayData);
            NetworkAPI.RegisterEvent<pMapperTrackerXRayStatus>(typeof(pMapperTrackerXRayStatus).FullName, MapperTrackerController.ReceiveMapperXRayStatus);
        }

        [ArchivePatch(typeof(XRayRenderer), null, new Type[] { typeof(IntPtr) }, ArchivePatch.PatchMethodType.Constructor)]
        private class XRayRenderer__ctor__Patch
        {
            private static void Postfix(XRayRenderer __instance)
            {
                __instance.instanceCount = 100000;
            }
        }

        [ArchivePatch(typeof(XRays), nameof(XRays.Update))]
        private class XRays__Update__Patch
        {
            private static bool Prefix(XRays __instance)
            {
                try
                {
                    int num = Mathf.CeilToInt(__instance.raysPerSecond * Mathf.Min(0.05f, Time.deltaTime));
                    __instance.Cast(num);
                    __instance.m_renderer.range = __instance.maxDistance;
                    if (MapperTrackerController.MapperTrackerXRaysInstanceIDLookup.TryGetValue(__instance.GetInstanceID(), out var controller))
                    {
                        __instance.m_renderer.mode = controller.IsSyncFocused ? 1 : 0;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        [ArchivePatch(typeof(EnemyScanner), nameof(EnemyScanner.OnWield))]
        private class EnemyScanner__OnWield__Patch
        {
            private static void Postfix(EnemyScanner __instance)
            {
                if (__instance == null)
                {
                    return;
                }
                GameObject gameObject = __instance.gameObject;
                MapperTrackerController activator = gameObject.GetComponent<MapperTrackerController>();
                if (activator == null)
                {
                    activator = gameObject.AddComponent<MapperTrackerController>();
                    activator.Setup(__instance, __instance.Owner);
                }
                activator.OnWield();
            }
        }

        [ArchivePatch(typeof(EnemyScanner), nameof(EnemyScanner.OnUnWield))]
        private class EnemyScanner__OnUnWield__Patch
        {
            private static void Prefix(EnemyScanner __instance)
            {
                GameObject gameObject = __instance.gameObject;
                MapperTrackerController activator = gameObject.GetComponent<MapperTrackerController>();
                if (activator != null)
                {
                    activator.OnUnWield();
                }
            }
        }

        [ArchivePatch(typeof(PlayerInventorySynced), nameof(PlayerInventorySynced.SyncWieldItem))]
        private class PlayerInventorySynced__SyncWieldItem__Patch
        {
            private static void Prefix(PlayerInventorySynced __instance, ItemEquippable item)
            {
                if (item == null || __instance.Owner.Owner.IsLocal)
                {
                    return;
                }
                if (__instance.WieldedItem == null)
                {
                    return;
                }
                GameObject gameObject = __instance.WieldedItem.gameObject;
                MapperTrackerController activator = gameObject.GetComponent<MapperTrackerController>();
                if (!CanDoMapper(item))
                {
                    if (activator != null)
                    {
                        activator.OnUnWield();
                    }
                    return;
                }
            }

            private static void Postfix(PlayerInventorySynced __instance, ItemEquippable item)
            {
                if (item == null || __instance.Owner.Owner.IsLocal || !CanDoMapper(item))
                {
                    return;
                }
                GameObject gameObject = item.gameObject;
                MapperTrackerController activator = gameObject.GetComponent<MapperTrackerController>();
                if (activator == null)
                {
                    activator = gameObject.AddComponent<MapperTrackerController>();
                }
                activator.Setup(item, __instance.Owner);
                activator.OnWield();
            }
        }

        private static bool CanDoMapper(ItemEquippable itemEquippable)
        {
            return itemEquippable.ItemDataBlock != null && itemEquippable.ItemDataBlock.inventorySlot == InventorySlot.GearClass 
                && itemEquippable.GearCategoryData.persistentID == 9;
        }

        public class MapperTrackerController : MonoBehaviour
        {
            public void Setup(ItemEquippable itemEquippable, PlayerAgent owner)
            {
                m_tool = itemEquippable;
                m_xRays = m_tool.GetComponentInChildren<XRays>(true);
                ToggleKey = Settings.ToggleKey;
                m_owner = owner;
                m_isValid = m_xRays != null && m_owner != null && m_owner.Owner != null;

                if (!m_isValid)
                {
                    return;
                }

                IsLocallyOwned = m_owner.Owner.IsLocal;
                OwnerLookup = m_owner.Owner.Lookup;

                m_mapperTrackerXRayStatusSynced = new();
                m_mapperTrackerXRayStatusSynced.enabled = false;
                m_mapperTrackerXRayStatusSynced.focus = false;
                m_mapperTrackerXRayStatusSynced.player.SetPlayer(m_owner.Owner);

                m_mapperTrackerXRayDataSynced = new();
                m_mapperTrackerXRayDataSynced.player.SetPlayer(m_owner.Owner);
                m_mapperTrackerXRayDataSynced.fieldOfView = Settings.FieldOfView;
                m_mapperTrackerXRayDataSynced.fieldOfViewFocused = Settings.FieldOfViewFocused;
                m_mapperTrackerXRayDataSynced.maxDistanceDefault = Settings.MaxDistanceDefault;
                m_mapperTrackerXRayDataSynced.maxDistanceFocused = Settings.MaxDistanceFocused;
                m_mapperTrackerXRayDataSynced.raysPerSecondDefault = Settings.RaysPerSecondDefault;
                m_mapperTrackerXRayDataSynced.raysPerSecondFocused = Settings.RaysPerSecondFocused;

                SetupXRays();

                MapperTrackerControllerLookup.TryAdd(OwnerLookup, this);
                MapperTrackerXRaysInstanceIDLookup.TryAdd(m_xRays.GetInstanceID(), this);
            }

            public void SetupXRays()
            {
                m_xRays.gameObject.active = false;
                m_xRays.fieldOfView = Settings.FieldOfView;
                m_xRays.fieldOfViewFocused = Settings.FieldOfViewFocused;
                m_xRays.maxDistance = Settings.MaxDistanceDefault;
                m_xRays.raysPerSecond = Settings.RaysPerSecondDefault;
                m_xRays.defaultColor = Settings.DefaultColor.ToUnityColor();
                m_xRays.defaultSize = Settings.DefaultSize;
                m_xRays.enemyColor = Settings.EnemyColor.ToUnityColor();
                m_xRays.enemySize = Settings.EnemySize;
                m_xRays.interactionColor = Settings.InteractionColor.ToUnityColor();
                m_xRays.interactionSize = Settings.InteractionSize;
                if (m_xRays.m_renderer == null)
                {
                    m_xRays.m_renderer = m_xRays.gameObject.GetComponent<XRayRenderer>();
                }
            }

            public void UpdateXRaysStatus()
            {
                m_xRays.gameObject.active = m_mapperTrackerXRayStatusSynced.enabled;
                m_xRays.fieldOfView = IsSyncFocused ? m_mapperTrackerXRayDataSynced.fieldOfViewFocused : m_mapperTrackerXRayDataSynced.fieldOfView;
                m_xRays.maxDistance = IsSyncFocused ? m_mapperTrackerXRayDataSynced.maxDistanceFocused : m_mapperTrackerXRayDataSynced.maxDistanceDefault;
                m_xRays.raysPerSecond = IsSyncFocused ? m_mapperTrackerXRayDataSynced.raysPerSecondFocused : m_mapperTrackerXRayDataSynced.raysPerSecondDefault;
                m_statusNeedUpdate = false;
            }

            public void UpdateXRaysData()
            {
                m_xRays.fieldOfView = IsSyncFocused ? m_mapperTrackerXRayDataSynced.fieldOfViewFocused : m_mapperTrackerXRayDataSynced.fieldOfView;
                m_xRays.fieldOfViewFocused = m_mapperTrackerXRayDataSynced.fieldOfViewFocused;
                m_xRays.maxDistance = IsSyncFocused ? m_mapperTrackerXRayDataSynced.maxDistanceFocused : m_mapperTrackerXRayDataSynced.maxDistanceDefault;
                m_xRays.raysPerSecond = IsSyncFocused ? m_mapperTrackerXRayDataSynced.raysPerSecondFocused : m_mapperTrackerXRayDataSynced.raysPerSecondDefault;
            }

            private void OnDestroy()
            {
                MapperTrackerControllerLookup.Remove(OwnerLookup);
                MapperTrackerXRaysInstanceIDLookup.Remove(m_xRays.GetInstanceID());
            }

            // OnWield时进行XRays参数与状态同步
            public void OnWield()
            {
                if (!m_isValid)
                {
                    return;
                }
                SetupXRays();
                enabled = true;
                MapperTrackerControllerLookup.TryAdd(OwnerLookup, this);
                MapperTrackerXRaysInstanceIDLookup.TryAdd(m_xRays.GetInstanceID(), this);
                if (IsLocallyOwned)
                {
                    SendMapperTrackerXRayStatus(false, false);
                    SendMapperTrackerXRayData(Settings.FieldOfView, Settings.FieldOfViewFocused, Settings.MaxDistanceDefault, Settings.MaxDistanceFocused,
                        Settings.RaysPerSecondDefault, Settings.RaysPerSecondFocused);
                }
            }

            // OnUnWield时进行XRays状态同步
            public void OnUnWield()
            {
                if (!m_isValid)
                {
                    return;
                }
                m_xRays.gameObject.active = false;
                if (IsLocallyOwned)
                {
                    SendMapperTrackerXRayStatus(false, false);
                }
            }

            // Update方法用于本地按键监听
            private void Update()
            {
                // 排除Owner不是自身的情况
                if (!m_isValid || !IsWielded || !IsLocallyOwned)
                {
                    return;
                }
                if (!Settings.EnableMapperTracker)
                {
                    return;
                }
                if (IsSyncFocused != IsLocalFireButtomHold)
                {
                    m_mapperTrackerXRayStatusSynced.focus = !m_mapperTrackerXRayStatusSynced.focus;
                    m_statusNeedUpdate = true;
                    m_statusNeedSync = true;
                    m_statusFocusChanged = true;
                }
                if (Input.GetKeyDown(ToggleKey))
                {
                    m_mapperTrackerXRayStatusSynced.enabled = !m_mapperTrackerXRayStatusSynced.enabled;
                    m_statusNeedUpdate = true;
                    m_statusNeedSync = true;
                }
            }

            // FixedUpdate方法用于状态更新
            private void FixedUpdate()
            {
                if (!m_isValid)
                {
                    return;
                }
                // Focus状态改变后重置XRayRenderer
                if (m_statusFocusChanged)
                {
                    TryClearXRays();
                    m_statusFocusChanged = false;
                }
                if (m_statusNeedUpdate)
                {
                    UpdateXRaysStatus();
                }
                if (Settings.EnableMapperTracker && IsLocallyOwned && m_statusNeedSync)
                {
                    m_statusNeedSync = false;
                    SendMapperTrackerXRayStatus(IsXRayEnabled, IsLocalFireButtomHold);
                }
            }

            private void OnDisable()
            {
                this.SafeDestroy();
            }

            private void TryClearXRays()
            {
                if (m_xRays.m_renderer != null)
                {
                    m_xRays.m_renderer.DeallocateResources();
                }
            }

            public static void ReceiveMapperXRayStatus(ulong sender, pMapperTrackerXRayStatus data)
            {
                if (data.player.TryGetPlayer(out SNet_Player player) && player.Lookup == sender)
                {
                    if (MapperTrackerControllerLookup.TryGetValue(sender, out var activator))
                    {
                        activator.transform.parent.gameObject.SetActive(true);
                        activator.m_statusFocusChanged = activator.m_mapperTrackerXRayStatusSynced.focus != data.focus;
                        activator.m_mapperTrackerXRayStatusSynced = data;
                        activator.m_statusNeedUpdate = true;
                        activator.m_statusFocusChanged = true;
                    }
                }
            }

            public static void ReceiveMapperXRayData(ulong sender, pMapperTrackerXRayData data)
            {
                if (data.player.TryGetPlayer(out SNet_Player player) && player.Lookup == sender)
                {
                    if (MapperTrackerControllerLookup.TryGetValue(sender, out var activator))
                    {
                        activator.m_mapperTrackerXRayDataSynced = data;
                        activator.UpdateXRaysData();
                    }
                }
            }

            public static void SendLocalData()
            {
                
            }

            public void SendMapperTrackerXRayStatus(bool enable, bool focus)
            {
                m_mapperTrackerXRayStatusSynced.enabled = enable;
                m_mapperTrackerXRayStatusSynced.focus = focus;
                NetworkAPI.InvokeEvent(typeof(pMapperTrackerXRayStatus).FullName, m_mapperTrackerXRayStatusSynced, SNet_ChannelType.GameNonCritical);
            }

            public void SendMapperTrackerXRayData(float fov, float fovFocused, float maxDistanceDefault, float maxDistanceFocused, 
                int raysPerSecondDefault, int raysPerSecondFocused)
            {
                m_mapperTrackerXRayDataSynced.fieldOfView = fov;
                m_mapperTrackerXRayDataSynced.fieldOfViewFocused = fovFocused;
                m_mapperTrackerXRayDataSynced.maxDistanceDefault = maxDistanceDefault;
                m_mapperTrackerXRayDataSynced.maxDistanceFocused = maxDistanceFocused;
                m_mapperTrackerXRayDataSynced.raysPerSecondDefault = raysPerSecondDefault;
                m_mapperTrackerXRayDataSynced.raysPerSecondFocused = raysPerSecondFocused;
                NetworkAPI.InvokeEvent(typeof(pMapperTrackerXRayData).FullName, m_mapperTrackerXRayDataSynced, SNet_ChannelType.GameNonCritical);
            }

            public static Dictionary<ulong, MapperTrackerController> MapperTrackerControllerLookup = new();

            public static Dictionary<int, MapperTrackerController> MapperTrackerXRaysInstanceIDLookup = new();

            public bool IsWielded => m_tool.m_isWielded;

            public bool IsLocalFireButtomHold => Input.GetKey(KeyCode.Mouse0);

            public bool IsSyncEnabled => m_mapperTrackerXRayStatusSynced.enabled;

            public bool IsSyncFocused => m_mapperTrackerXRayStatusSynced.focus;

            public bool IsLocallyOwned;

            private ItemEquippable m_tool;

            private ulong OwnerLookup;

            private PlayerAgent m_owner;

            private pMapperTrackerXRayData m_mapperTrackerXRayDataSynced;

            private pMapperTrackerXRayStatus m_mapperTrackerXRayStatusSynced;

            public static KeyCode ToggleKey = KeyCode.X;

            private bool IsXRayEnabled => m_xRays.gameObject.active;

            private XRays m_xRays;

            private bool m_isValid;

            private bool m_statusNeedSync;

            private bool m_statusNeedUpdate;

            private bool m_statusFocusChanged;
        }
    }
}

namespace Hikaria.MapperTracker
{
    public struct pMapperTrackerXRayStatus
    {
        public pMapperTrackerXRayStatus()
        {
            player = new();
            enabled = false;
            focus = false;
        }

        public bool enabled = false;

        public bool focus = false;

        public SNetStructs.pPlayer player = new();
    }

    public struct pMapperTrackerXRayData
    {
        public SNetStructs.pPlayer player = new();

        public float fieldOfView = 65f;

        public float fieldOfViewFocused = 35f;

        public float maxDistanceDefault = 20f;

        public float maxDistanceFocused = 40f;

        public int raysPerSecondDefault = 50000;

        public int raysPerSecondFocused = 10000;

        public pMapperTrackerXRayData()
        {
            player = new();
            fieldOfView = 65f;
            fieldOfViewFocused = 35f;
            maxDistanceDefault = 20f;
            maxDistanceFocused = 40f;
            raysPerSecondDefault = 75000;
            raysPerSecondFocused = 20000;
        }
    }
}