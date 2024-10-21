// Warning: Some assembly references could not be resolved automatically. This might lead to incorrect decompilation of some parts,
// for ex. property getter/setter access. To get optimal decompilation results, please manually add the missing references to the list of loaded assemblies.
// Se_web, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// TorchPlugin.Logic
using System;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI;
using TorchPlugin;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
namespace TorchPlugin
{
	public class Logic : MyGameLogicComponent
	{
		private bool m_closed = false;

		private MyObjectBuilder_EntityBase m_objectBuilder;

		public long m_attackerId;

		private bool m_init = false;

		private BlockDamageData savemessage = new BlockDamageData();

		public override void Init(MyObjectBuilder_EntityBase objectBuilder)
		{
			m_objectBuilder = objectBuilder;
			m_attackerId = 0L;
			base.Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
			base.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME | MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
		}

		public void UpdateNetworkBalanced()
		{
			if (!m_closed && base.Entity.InScene && MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Multiplayer.IsServer)
			{
				byte[] array = new byte[16];
				byte[] bytes = BitConverter.GetBytes(base.Entity.EntityId);
				byte[] bytes2 = BitConverter.GetBytes(m_attackerId);
				for (int i = 0; i < 8; i++)
				{
					array[i] = bytes[i];
				}
				for (int j = 0; j < 8; j++)
				{
					array[j + 8] = bytes2[j];
				}
				MyAPIGateway.Multiplayer.SendMessageToOthers(5859, array);
			}
		}

		public override void Close()
		{
			m_closed = true;
			IMyEntity entity = base.Entity;
			IMyTerminalBlock val = (IMyTerminalBlock)((entity is IMyTerminalBlock) ? entity : null);
		}

		private void InitStorage()
		{
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0024: Expected O, but got Unknown
			if (base.Entity.Storage == null)
			{
				base.Entity.Storage = (MyModStorageComponentBase)new MyModStorageComponent();
			}
		}

		private void LoadStorage()
		{
			if (!base.Entity.Storage.ContainsKey(BlockDamageData.StorageGuid))
			{
				return;
			}
			string value = base.Entity.Storage.GetValue(BlockDamageData.StorageGuid);
			try
			{
				BlockDamageData blockDamageData = MyAPIGateway.Utilities.SerializeFromBinary<BlockDamageData>(Convert.FromBase64String(value));
				m_attackerId = blockDamageData.attackerId;
			}
			catch (Exception)
			{
				SaveStorage();
			}
		}

		private void SaveStorage()
		{
			if (base.Entity.Storage == null)
			{
				InitStorage();
			}
			BlockDamageData obj = new BlockDamageData
			{
				attackerId = m_attackerId
			};
			byte[] inArray = MyAPIGateway.Utilities.SerializeToBinary(obj);
			base.Entity.Storage.SetValue(BlockDamageData.StorageGuid, Convert.ToBase64String(inArray));
		}

		public override void UpdateBeforeSimulation()
		{
			IMyCubeBlock myCubeBlock = base.Entity as IMyCubeBlock;
			if (!m_init)
			{
				m_init = true;
			}
			if (myCubeBlock != null)
			{
				IMyCubeGrid cubeGrid = myCubeBlock.CubeGrid;
				if (cubeGrid != null)
				{
					savemessage.attackerId = m_attackerId;
				}
			}
		}

		public override void UpdateOnceBeforeFrame()
		{
			InitStorage();
			LoadStorage();
			SaveStorage();
		}
	}
}