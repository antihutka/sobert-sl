using System;
using System.Collections.Concurrent;
using System.Threading;
using OpenMetaverse;

namespace NNBot
{
	public class NameCache
	{
		public NameCache ()
		{
		}

		private static ConcurrentDictionary<UUID, string> names = new ConcurrentDictionary<UUID, string>();
		private static ConcurrentBag<UUID> requestedNames = new ConcurrentBag<UUID>();
		private static ConcurrentDictionary<UUID, string> displayNames = new ConcurrentDictionary<UUID, string>();

		public static void recvName(UUID id, string name)
		{
			names [id] = name;
		}

		public static void requestName(UUID id)
		{
			if (names.ContainsKey (id))
				return;
			Bot.Client.Avatars.RequestAvatarName (id);
		}

		public static string getName(UUID id)
		{
			if (names.ContainsKey (id))
				return names [id];
			if (id == UUID.Zero)
				return "<zero>";
			requestName (id);
			for (int i = 0; i<25; i++) {
				Thread.Sleep (200);
				if (names.ContainsKey (id))
					return names [id];
			}
			Console.WriteLine ("Timed out getting name for " + id.ToString ());
			return "";
		}
	}
}
