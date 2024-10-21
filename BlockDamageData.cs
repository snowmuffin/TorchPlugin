

using System;
using System.ComponentModel;
using ProtoBuf;
namespace TorchPlugin
{
	[ProtoContract]
	public class BlockDamageData
	{
		public static readonly Guid StorageGuid = new Guid("E55D3AD6-DC2C-4829-AE41-326B97773AE4");

		[ProtoMember(1)]
		[DefaultValue(0)]
		public long attackerId;

		public BlockDamageData(long value1)
		{
			attackerId = value1;
		}

		public BlockDamageData()
		{
		}
	}
}