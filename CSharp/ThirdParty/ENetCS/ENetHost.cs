﻿#region License

/*
ENet for C#
Copyright (c) 2011 James F. Bellinger <jfb@zer7.com>

Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
*/

#endregion

using ELog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ENet
{
	public sealed unsafe class ENetHost : IDisposable
	{
		private Native.ENetHost* host;
		private PeerManager peerManager = new PeerManager();

		public ENetHost(ushort port, uint peerLimit):
			this(new Address { Port = port }, peerLimit)
		{
		}

		public ENetHost(Address? address, uint peerLimit, uint channelLimit = 0, 
				uint incomingBandwidth = 0, uint outgoingBandwidth = 0, bool enableCrc = true)
		{
			if (peerLimit > Native.ENET_PROTOCOL_MAXIMUM_PEER_ID)
			{
				throw new ArgumentOutOfRangeException("peerLimit");
			}
			CheckChannelLimit(channelLimit);

			if (address != null)
			{
				Native.ENetAddress nativeAddress = address.Value.NativeData;
				this.host = Native.enet_host_create(
					ref nativeAddress, peerLimit, channelLimit, incomingBandwidth,
					outgoingBandwidth);
			}
			else
			{
				this.host = Native.enet_host_create(
					null, peerLimit, channelLimit, incomingBandwidth,
					outgoingBandwidth);
			}

			if (this.host == null)
			{
				throw new ENetException(0, "Host creation call failed.");
			}

			if (enableCrc)
			{
				Native.enet_enable_crc(host);
			}
		}

		~ENetHost()
		{
			this.Dispose(false);
		}

		public void Dispose()
		{
			this.Dispose(true);
			GC.SuppressFinalize(this);
		}

		private void Dispose(bool disposing)
		{
			if (this.host == null)
			{
				return;
			}

			if (disposing)
			{
				Native.enet_host_destroy(this.host);
				this.host = null;
			}
		}

		public PeerManager Peers
		{
			get
			{
				return peerManager;
			}
		}

		private static void CheckChannelLimit(uint channelLimit)
		{
			if (channelLimit > Native.ENET_PROTOCOL_MAXIMUM_CHANNEL_COUNT)
			{
				throw new ArgumentOutOfRangeException("channelLimit");
			}
		}

		public void Broadcast(byte channelID, ref ENetPacket packet)
		{
			Native.enet_host_broadcast(this.host, channelID, packet.NativeData);
			packet.NativeData = null; // Broadcast automatically clears this.
		}

		public void CompressWithRangeEncoder()
		{
			Native.enet_host_compress_with_range_encoder(this.host);
		}

		public void DoNotCompress()
		{
			Native.enet_host_compress(this.host, null);
		}

		public int CheckEvents(out ENetEvent e)
		{
			Native.ENetEvent nativeEvent;
			int ret = Native.enet_host_check_events(this.host, out nativeEvent);
			e = new ENetEvent(this, nativeEvent);
			return ret;
		}

		public Task<ENetPeer> ConnectAsync(
			Address address, uint channelLimit = Native.ENET_PROTOCOL_MAXIMUM_CHANNEL_COUNT, 
			uint data = 0)
		{
			CheckChannelLimit(channelLimit);

			var tcs = new TaskCompletionSource<ENetPeer>();
			Native.ENetAddress nativeAddress = address.NativeData;
			Native.ENetPeer* p = Native.enet_host_connect(this.host, ref nativeAddress, channelLimit, data);
			if (p == null)
			{
				throw new ENetException(0, "Host connect call failed.");
			}
			new ENetPeer(this, p)
			{
				Connected = e => tcs.TrySetResult(e.ENetPeer)
			};
			return tcs.Task;
		}

		public void Flush()
		{
			Native.enet_host_flush(this.host);
		}

		public int Service(int timeout)
		{
			if (timeout < 0)
			{
				throw new ArgumentOutOfRangeException("timeout");
			}
			return Native.enet_host_service(this.host, null, (uint) timeout);
		}

		public int Service(int timeout, out ENetEvent e)
		{
			if (timeout < 0)
			{
				throw new ArgumentOutOfRangeException("timeout");
			}
			Native.ENetEvent nativeEvent;

			int ret = Native.enet_host_service(this.host, out nativeEvent, (uint) timeout);
			e = new ENetEvent(this, nativeEvent);
			return ret;
		}

		public void SetBandwidthLimit(uint incomingBandwidth, uint outgoingBandwidth)
		{
			Native.enet_host_bandwidth_limit(this.host, incomingBandwidth, outgoingBandwidth);
		}

		public void SetChannelLimit(uint channelLimit)
		{
			CheckChannelLimit(channelLimit);
			Native.enet_host_channel_limit(this.host, channelLimit);
		}

		public void Run()
		{
			if (this.Service(0) < 0)
			{
				return;
			}

			ENetEvent e;
			while (this.CheckEvents(out e) > 0)
			{
				switch (e.Type)
				{
					case EventType.Connect:
					{
						e.ENetPeer.Connected(e);
						break;
					}
					case EventType.Receive:
					{
						e.ENetPeer.Received(e);
						break;
					}
					case EventType.Disconnect:
					{
						Log.Debug("Disconnect");
						e.ENetPeer.Disconnect(e);
						break;
					}
				}
			}
		}
	}
}