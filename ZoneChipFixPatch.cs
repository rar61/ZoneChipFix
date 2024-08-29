using System;
using System.Reflection;
using NLog;
using Sandbox;
using Sandbox.Engine.Multiplayer;
using Sandbox.Engine.Utils;
using Sandbox.Game;
using Sandbox.Game.Entities;
using SpaceEngineers.Game.Definitions.SafeZone;
using SpaceEngineers.Game.Entities.Blocks.SafeZone;
using Torch.Managers.PatchManager;
using Torch.Utils;
using VRage;
using VRage.Game;
using VRage.Game.ObjectBuilders.Components.Beacon;
using VRage.Game.ObjectBuilders.ComponentSystem;
using VRage.Sync;

namespace ZoneChipFix
{
    [PatchShim]
    static class ZoneChipFixPatch
    {
        static readonly Logger log = LogManager.GetCurrentClassLogger();

        static readonly MyDefinitionId ZoneChipDefinition = new MyDefinitionId(typeof(MyObjectBuilder_Component), "ZoneChip");

#pragma warning disable CS0649
        [ReflectedMethodInfo(typeof(MySafeZoneComponent), "TryConsumeUpkeep")]
        static readonly MethodInfo tryConsumeUpkeep;
        [ReflectedMethodInfo(typeof(ZoneChipFixPatch), nameof(TryConsumeUpkeepPatch))]
        static readonly MethodInfo tryConsumeUpkeepPatch;
        [ReflectedMethodInfo(typeof(MySafeZoneComponent), "Serialize")]
        static readonly MethodInfo serialize;
        [ReflectedMethodInfo(typeof(ZoneChipFixPatch), nameof(SerializePatch))]
        static readonly MethodInfo serializePatch;

        [ReflectedGetter(Name = "Definition")]
        static readonly Func<MySafeZoneBlock, MySafeZoneBlockDefinition> GetSafezoneDefinition;
        [ReflectedMethodInfo(typeof(MySafeZoneComponent), "SetUpkeepCoundown_Client")]
        static readonly MethodInfo setUpkeepCoundownClient;
#pragma warning restore CS0649

        static void Patch(PatchContext context)
        {
            context.GetPattern(tryConsumeUpkeep).Prefixes.Add(tryConsumeUpkeepPatch);
            context.GetPattern(serialize).Suffixes.Add(serializePatch);
        }

        static bool TryConsumeUpkeepPatch(MySafeZoneComponent __instance, MySafeZoneBlock __field_m_parentBlock, ref TimeSpan __field_m_upkeepTime, ref bool __result)
        {
            __result = TryConsumeUpkeepPatchImpl(__instance, __field_m_parentBlock, ref __field_m_upkeepTime);
            return false;
        }

        static bool TryConsumeUpkeepPatchImpl(MySafeZoneComponent instance, MySafeZoneBlock parentBlock, ref TimeSpan upkeepTime)
        {
            if (!MyFakes.ENABLE_ZONE_CHIP_REQ) return true;

            var totalPlayTime = new TimeSpan(MySandboxGame.TotalGamePlayTimeInMilliseconds * TimeSpan.TicksPerMillisecond);

            var remainingTime = upkeepTime - totalPlayTime;
            remainingTime = -remainingTime;

            var safeZoneUpkeepTime = new TimeSpan(GetSafezoneDefinition(parentBlock).SafeZoneUpkeepTimeM * TimeSpan.TicksPerMinute);
            if (safeZoneUpkeepTime == TimeSpan.Zero) return false;

            var safeZoneUpkeep = GetSafezoneDefinition(parentBlock).SafeZoneUpkeep;
            if (safeZoneUpkeep == 0) return true;

            MyInventory inventory = MyEntityExtensions.GetInventory(parentBlock);
            if (inventory == null) return false;

            if (instance.SafeZoneEntityId != 0 && MyEntities.GetEntityById(instance.SafeZoneEntityId) is MySafeZone safeZone && safeZone.Enabled)
            {
                var zoneChipsRequired = new MyFixedPoint() { RawValue = (remainingTime.Ticks / safeZoneUpkeepTime.Ticks * safeZoneUpkeep + safeZoneUpkeep) * 1_000_000 };
                if (!ConsumeZoneChips(zoneChipsRequired, inventory, parentBlock)) return false;
                upkeepTime = safeZoneUpkeepTime - new TimeSpan(remainingTime.Ticks % safeZoneUpkeepTime.Ticks) + totalPlayTime;
            }
            else
            {
                var zoneChipsRequired = new MyFixedPoint() { RawValue = safeZoneUpkeep * 1_000_000 };
            
                if (!TryConsumeZoneChips(zoneChipsRequired, inventory, parentBlock)) return false;
                upkeepTime = safeZoneUpkeepTime + totalPlayTime;
            }

            MyMultiplayer.RaiseEvent(instance, component => (Action<double>)setUpkeepCoundownClient.CreateDelegate(typeof(Action<double>), component), safeZoneUpkeepTime.Minutes);

            return true;
        }

        static bool ConsumeZoneChips(MyFixedPoint amount, MyInventory inventory, MySafeZoneBlock safeZoneBlock)
        {
            amount -= inventory.RemoveItemsOfType(amount, ZoneChipDefinition);
            if (amount > MyFixedPoint.Zero)
            {
                amount -= safeZoneBlock.CubeGrid.GridSystems.ConveyorSystem.PullItem(ZoneChipDefinition, amount, safeZoneBlock, inventory, true, MyFakes.CONV_PULL_CACL_IMMIDIATLY_STORE_SAFEZONE);
            }

            return amount <= MyFixedPoint.Zero;
        }

        static bool TryConsumeZoneChips(MyFixedPoint amount, MyInventory inventory, MySafeZoneBlock safeZoneBlock)
        {
            var remainder = amount - inventory.GetItemAmount(ZoneChipDefinition);
            if (remainder <= MyFixedPoint.Zero)
            {
                inventory.RemoveItemsOfType(amount, ZoneChipDefinition);
                return true;
            }
            else if (safeZoneBlock.CubeGrid.GridSystems.ConveyorSystem.PullItem(ZoneChipDefinition, remainder, safeZoneBlock, inventory, false, MyFakes.CONV_PULL_CACL_IMMIDIATLY_STORE_SAFEZONE) >= remainder)
            {
                inventory.RemoveItemsOfType(amount, ZoneChipDefinition);
                return true;
            }

            return false;
        }

        static void SerializePatch(Sync<long, SyncDirection.FromServer> __field_m_safeZoneEntityId, TimeSpan __field_m_timeLeft, MyObjectBuilder_ComponentBase __result)
        {
            if (__field_m_safeZoneEntityId != 0 && MyEntities.GetEntityById(__field_m_safeZoneEntityId) is MySafeZone safeZone && safeZone.Enabled) return;

            if (__result is MyObjectBuilder_SafeZoneComponent builder)
            {
                builder.UpkeepTime = __field_m_timeLeft.TotalMilliseconds;
            }
            else
            {
                log.Warn("Cannot assign proper upkeep time, object builder is of wrong type.");
            }
        }
    }
}