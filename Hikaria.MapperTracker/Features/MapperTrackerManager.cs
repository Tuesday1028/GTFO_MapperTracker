using CellMenu;
using Gear;
using GTFO.API;
using Player;
using SNetwork;
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
        public override string Name => "映射追踪器";

        public override string Group => FeatureGroups.GetOrCreate("映射追踪器");

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
            public float FieldOfView { get; set; } = 60f;
            [FSDisplayName("聚焦扫描视野")]
            [FSDescription("同步")]
            public float FieldOfViewFocused { get; set; } = 25f;
            [FSDisplayName("默认扫描最大距离")]
            [FSDescription("同步")]
            public float MaxDistanceDefault { get; set; } = 25f;
            [FSDisplayName("聚焦扫描最大距离")]
            [FSDescription("同步")]
            public float MaxDistanceFocused { get; set; } = 50f;
            [FSDisplayName("扫描范围与正方形的近似程度")]
            [FSDescription("同步，范围0 - 1")]
            public float Square { get; set; } = 0.2f;
            [FSDisplayName("X-Ray每秒更新数量")]
            [FSDescription("根据自身喜好与设备性能调节，范围1000-20000，建议2000")]
            public int RaysPerSecond { get; set; } = 2000;
            [FSDisplayName("起始距离")]
            [FSDescription("建议范围1-2")]
            public float ForwardStepSize { get; set; } = 1.5f;
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


        [ArchivePatch(typeof(EnemyScanner), nameof(EnemyScanner.OnWield))]
        private class EnemyScanner__OnWield__Patch
        {
            private static void Postfix(EnemyScanner __instance)
            {
                if (CurrentGameState != (int)eGameStateName.InLevel)
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
                if (CurrentGameState != (int)eGameStateName.InLevel)
                {
                    return;
                }
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
            private static void Postfix(PlayerInventorySynced __instance, ItemEquippable item)
            {
                if (GameStateManager.CurrentStateName != eGameStateName.InLevel || item == null || __instance.Owner.Owner.IsLocal)
                {
                    return;
                }
                GameObject gameObject = item.gameObject;
                MapperTrackerController activator = gameObject.GetComponent<MapperTrackerController>();
                if (!CanDoMapper(item))
                {
                    if (activator != null)
                    {
                        activator.OnUnWield();
                    }
                    return;
                }
                if (activator == null)
                {
                    activator = gameObject.AddComponent<MapperTrackerController>();
                    activator.Setup(item, __instance.Owner);
                }
                activator.OnWield();
            }
        }

        //XRaysRenderer Mode Fix
        [ArchivePatch(typeof(XRays), nameof(XRays.Update))]
        private class XRays__Update__Patch
        {
            private static void Postfix(XRays __instance)
            {
                if (MapperTrackerController.MapperTrackerXRaysInstanceIDLookup.TryGetValue(__instance.GetInstanceID(), out var controller))
                {
                    __instance.m_renderer.mode = controller.IsLocalSyncFocused ? 0 : 1;
                }
            }      
        }

        private static bool CanDoMapper(ItemEquippable itemEquippable)
        {
            return itemEquippable.ItemDataBlock != null && itemEquippable.ItemDataBlock.inventorySlot == InventorySlot.GearClass && itemEquippable.GetComponentInChildren<XRays>() != null;
        }

        public class MapperTrackerController : MonoBehaviour
        {
            public void Setup(ItemEquippable itemEquippable, PlayerAgent owner)
            {
                m_tool = itemEquippable;
                m_xRays = m_tool.GetComponentInChildren<XRays>(true);
                ToggleKey = Settings.ToggleKey;
                m_owner = owner;
                m_isValid = m_xRays != null && m_owner != null;
                if (!m_isValid)
                {
                    return;
                }

                m_mapperTrackerXRayStatus = new();
                m_mapperTrackerXRayStatus.focus = false;
                m_mapperTrackerXRayStatus.player.SetPlayer(m_owner.Owner);

                m_mapperTrackerXRayData = new();
                m_mapperTrackerXRayData.player.SetPlayer(m_owner.Owner);
                m_mapperTrackerXRayData.fieldOfView = Settings.FieldOfView;
                m_mapperTrackerXRayData.fieldOfViewFocused = Settings.FieldOfViewFocused;
                m_mapperTrackerXRayData.maxDistanceDefault = Settings.MaxDistanceDefault;
                m_mapperTrackerXRayData.maxDistanceFocused = Settings.MaxDistanceFocused;
                m_mapperTrackerXRayData.square = Settings.Square;

                SetupXRays();

                MapperTrackerControllerLookup.TryAdd(m_owner.Owner.Lookup, this);
                MapperTrackerXRaysInstanceIDLookup.TryAdd(m_xRays.GetInstanceID(), this);
            }

            public void SetupXRays()
            {
                m_xRays.fieldOfView = Settings.FieldOfView;
                m_xRays.fieldOfViewFocused = Settings.FieldOfViewFocused;
                m_xRays.maxDistance = Settings.MaxDistanceDefault;
                m_xRays.square = Settings.Square;
                m_xRays.raysPerSecond = Settings.RaysPerSecond;
                m_xRays.defaultColor = Settings.DefaultColor.ToUnityColor();
                m_xRays.defaultSize = Settings.DefaultSize;
                m_xRays.enemyColor = Settings.EnemyColor.ToUnityColor();
                m_xRays.enemySize = Settings.EnemySize;
                m_xRays.interactionColor = Settings.InteractionColor.ToUnityColor();
                m_xRays.interactionSize = Settings.InteractionSize;
            }

            private void OnDestroy()
            {
                MapperTrackerControllerLookup.Remove(m_owner.Owner.Lookup);
                MapperTrackerXRaysInstanceIDLookup.Remove(m_xRays.GetInstanceID());
            }

            // OnWield时进行XRays参数与状态同步
            public void OnWield()
            {
                if (!m_isValid)
                {
                    enabled = false;
                    return;
                }
                SetupXRays();
                enabled = true;
                if (m_owner.Owner.IsLocal)
                {
                    SendAndUpdateMapperTrackerXRayStatus(false, false);
                }
                SendAndUpdateMapperTrackerXRayData(Settings.FieldOfView, Settings.FieldOfViewFocused, Settings.MaxDistanceDefault, Settings.MaxDistanceFocused, Settings.Square);
            }

            // OnUnWield时进行XRays状态同步
            public void OnUnWield()
            {
                if (!m_isValid)
                {
                    enabled = false;
                    return;
                }
                SetupXRays();
                enabled = false;
                XRayEnabled = false;
                if (m_owner.Owner.IsLocal)
                {
                    SendAndUpdateMapperTrackerXRayStatus(false, false);
                }
            }

            // Update方法用于本地按键监听
            private void Update()
            {
                if (!m_isValid || !IsLocalSyncWielded || !m_owner.Owner.IsLocal)
                {
                    return;
                }
                if (!Settings.EnableMapperTracker)
                {
                    return;
                }
                IsLocalSyncFocused = IsLocalFireButtomHold;
                if (Input.GetKeyDown(ToggleKey))
                {
                    XRayEnabled = !XRayEnabled;
                }
            }

            // FixedUpdate方法用于状态更新
            private void FixedUpdate()
            {
                if (!m_isValid)
                {
                    return;
                }
                // 当同步接收的状态与当前状态不同时进行同步
                if (!m_owner.Owner.IsLocal && m_statusSyncedNeedUpdate)
                {
                    m_statusSyncedNeedUpdate = false;
                    XRayEnabled = m_mapperTrackerXRayStatus.enabled;
                    m_xRays.fieldOfView = IsLocalSyncFocused ? m_mapperTrackerXRayData.fieldOfViewFocused : m_mapperTrackerXRayData.fieldOfView;
                    m_xRays.maxDistance = IsLocalSyncFocused ? m_mapperTrackerXRayData.maxDistanceFocused : m_mapperTrackerXRayData.maxDistanceDefault;
                }
                if (Settings.EnableMapperTracker && m_owner.Owner.IsLocal && m_statusNeedSync)
                {
                    m_statusNeedSync = false;
                    m_xRays.fieldOfView = IsLocalSyncFocused ? m_mapperTrackerXRayData.fieldOfViewFocused : m_mapperTrackerXRayData.fieldOfView;
                    m_xRays.maxDistance = IsLocalSyncFocused ? m_mapperTrackerXRayData.maxDistanceFocused : m_mapperTrackerXRayData.maxDistanceDefault;
                    SendAndUpdateMapperTrackerXRayStatus(XRayEnabled, IsLocalFireButtomHold);
                }
            }

            public static void ReceiveMapperXRayStatus(ulong sender, pMapperTrackerXRayStatus data)
            {
                if (data.player.TryGetPlayer(out SNet_Player player) && player.Lookup == sender)
                {
                    if (MapperTrackerControllerLookup.TryGetValue(sender, out var activator))
                    {
                        activator.m_mapperTrackerXRayStatus = data;
                        activator.XRayEnabled = data.enabled;
                    }
                }
            }

            public static void ReceiveMapperXRayData(ulong sender, pMapperTrackerXRayData data)
            {
                if (data.player.TryGetPlayer(out SNet_Player player) && player.Lookup == sender)
                {
                    if (MapperTrackerControllerLookup.TryGetValue(sender, out var activator))
                    {
                        activator.m_mapperTrackerXRayData = data;
                    }
                }
            }

            public void SendAndUpdateMapperTrackerXRayStatus(bool enable, bool focus)
            {
                m_mapperTrackerXRayStatus.enabled = enable;
                m_mapperTrackerXRayStatus.focus = focus;
                NetworkAPI.InvokeEvent(typeof(pMapperTrackerXRayStatus).FullName, m_mapperTrackerXRayStatus, SNet_ChannelType.GameNonCritical);
            }

            public void SendAndUpdateMapperTrackerXRayData(float fov, float fovFocused, float maxDistanceDefault, float maxDistanceFocused, float square)
            {
                m_mapperTrackerXRayData.fieldOfView = fov;
                m_mapperTrackerXRayData.fieldOfViewFocused = fovFocused;
                m_mapperTrackerXRayData.maxDistanceDefault = maxDistanceDefault;
                m_mapperTrackerXRayData.maxDistanceFocused = maxDistanceFocused;
                m_mapperTrackerXRayData.square = square;
                NetworkAPI.InvokeEvent(typeof(pMapperTrackerXRayData).FullName, m_mapperTrackerXRayData, SNet_ChannelType.GameNonCritical);
            }

            public static Dictionary<ulong, MapperTrackerController> MapperTrackerControllerLookup = new();

            public static Dictionary<int, MapperTrackerController> MapperTrackerXRaysInstanceIDLookup = new();

            public bool IsLocalSyncWielded => m_tool.m_isWielded;

            public bool IsLocalFireButtomHold => m_tool.FireButton;

            public bool IsLocalSyncFocused
            {
                get
                {
                    return m_mapperTrackerXRayStatus.focus;
                }
                set
                {
                    if (m_mapperTrackerXRayStatus.focus != value && m_owner.Owner.IsLocal)
                    {
                        m_statusNeedSync = true;
                    }
                    m_mapperTrackerXRayStatus.focus = value;
                }
            }

            private ItemEquippable m_tool;

            private PlayerAgent m_owner;

            private pMapperTrackerXRayData m_mapperTrackerXRayData;

            private pMapperTrackerXRayStatus m_mapperTrackerXRayStatus;

            public static KeyCode ToggleKey = KeyCode.X;

            private bool XRayEnabled
            {
                get
                {
                    return m_xRays.gameObject.active;
                }
                set
                {
                    if (m_xRays.gameObject.active != value && m_owner.Owner.IsLocal)
                    {
                        m_statusNeedSync = true;
                    }
                    m_xRays.gameObject.active = value;
                }
            }

            private XRays m_xRays;

            private bool m_isValid;

            private bool m_statusSyncedNeedUpdate;

            private bool m_statusNeedSync;
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

        public bool enabled;

        public bool focus = false;

        public SNetStructs.pPlayer player = new();
    }

    public struct pMapperTrackerXRayData
    {
        public SNetStructs.pPlayer player = new();

        public float fieldOfView = 65f;

        public float fieldOfViewFocused = 25f;

        public float maxDistanceDefault = 60f;

        public float maxDistanceFocused = 25f;

        public float square = 0.2f;

        public pMapperTrackerXRayData()
        {
            player = new();
            fieldOfView = 65f;
            fieldOfViewFocused = 25f;
            maxDistanceDefault = 30f;
            maxDistanceFocused = 30f;
            square = 0.2f;
        }
    }
}